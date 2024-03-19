import socket
import threading
import time
import gc
from tsss import TSSS
from tsss import TcpSubStream
from gobb import Gobb


#クライアントIDは0から始まり、ゲームサーバ内で使用される配列等のリーソース割り当て用連番です。
#プレイヤーIDは1から始まり、ゲーム内のプレイヤー管理や駒IDの上4ビットなどに使用される連番です。


#ゲームサーバ（ゲームサーバユニットの割り当てを行う）
class GobbGameServer:
    
    def __init__(self, sock:socket.socket, max_game_server_unit_num:int) -> None:
        self.__sock = sock
        self.__max_game_server_unit_num = max_game_server_unit_num

    def Run(self) -> None:

        tsss = TSSS(self.__sock, 2 * self.__max_game_server_unit_num)
        tsss.Start()

        game_server_id = 0
        game_server_list = [None for g in range(0, self.__max_game_server_unit_num)]
        while True:
            
            if game_server_list[game_server_id] is None:
                c = 2 * game_server_id
                game_server_list[game_server_id] = GobbGameServerUnit(game_server_id, [TcpSubStream(tsss, c), TcpSubStream(tsss, c + 1)])
                game_server_list[game_server_id].Start()
                while game_server_list[game_server_id].is_initializing_game:
                    time.sleep(1)
            else:
                if game_server_list[game_server_id].is_sleeping:
                    game_server_list[game_server_id] = None
                    gc.collect()
            
            game_server_id = (game_server_id + 1) % self.__max_game_server_unit_num
            


#個々のゲームサーバ
class GobbGameServerUnit:

    #クラス変数

    #アプリケーションデータタイプ
    __REQUEST_PARTICIPATION_APPROVAL = 0x00
    __REQUEST_COORDINATE_LIST = 0x01
    __REQUEST_PIECE_PLACEMENT = 0x02
    __ERROR_CLIENT = 0x03

    __RESPONSE_PARTICIPATION_APPROVAL = 0x10
    __RESPONSE_COORDINATE_LIST = 0x11
    __INSTRUCT_GAME_START = 0x12
    __INSTRUCT_PIECE_PICKING = 0x13
    __INSTRUCT_PIECE_PLACEMENT = 0x14
    __INSTRUCT_GAME_END = 0x15
    __ERROR_GAME = 0x16
    __ERROR_SERVER = 0x17
    
    #エラータイプ
    __UNKNOWN_ERROR_CLIENT = 0x00
    __CLIENT_DISCONNECTED = 0x01
    
    __UNKNOWN_ERROR_SERVER = 0x10
    __OTHER_CLIENT_HAS_UNKNOWN_ERROR = 0x11
    __OTHER_CLIENT_DISCONNECTED = 0x12

    __NON_EXISTENT_PIECE_ID = 0x20
    __NON_EXISTENT_COORDINATE = 0x21
    __PIECE_NOT_IN_YOUR_HAND = 0x22
    __NO_YOUR_PIECES_TO_SELECT = 0x23
    __NO_COORDINATES_TO_SELECT = 0x24
    __CANNOT_PLACE_PIECE = 0x25

    #エンドタイプ
    __WIN = 0x00
    __LOSE = 0x01
    __ERROR_END = 0x02

    #ステート
    __SLEEPING = 0x00 
    __INITIALIZING_GAME = 0x01
    __WAITING_PIECE_SELECTION = 0x02
    __WAITING_PIECE_PLACEMENT = 0x03
    __RESULT = 0x04


    #プロパティ
    is_sleeping = property(fget=lambda self: self.__state == GobbGameServerUnit.__SLEEPING)
    is_initializing_game = property(fget=lambda self: self.__state == GobbGameServerUnit.__INITIALIZING_GAME)
    is_waiting_piece_selection = property(fget=lambda self: self.__state == GobbGameServerUnit.__WAITING_PIECE_SELECTION)
    is_waiting_piece_placement = property(fget=lambda self: self.__state == GobbGameServerUnit.__WAITING_PIECE_PLACEMENT)
    is_result = property(fget=lambda self: self.__state == GobbGameServerUnit.__RESULT)


    def __init__(self, game_server_id:int, stream_list:list) -> None:

        print("GAME SERVER UNIT [id=" + str(game_server_id) + "] -> ALLOC")
        
        #ゲームサーバパラメタ
        self.__game_server_id = game_server_id
        self.__state = GobbGameServerUnit.__SLEEPING
        self.__client_item_list = {}
        self.__game = None
        self.__turn_client = 0
        self.__turn_player = 0

        for c in range(0, 2):
            self.__client_item_list[c] = {}
            self.__client_item_list[c]["stream"] = stream_list[c]


    def Start(self) -> None:
        self.__state = GobbGameServerUnit.__INITIALIZING_GAME
        self.__engine = threading.Thread(target=self.Run)
        self.__engine.setDaemon(True)
        self.__engine.start()


    def Run(self) -> None:
        
        print("GAME SERVER UNIT [id=" + str(self.__game_server_id) + "] -> RUN")

        #マッチング
        approval_thread = [[object] for i in range(0, 2)]
        for client_id in range(0, 2):
            self.__client_item_list[client_id]["approval"] = False
            approval_thread[client_id] = threading.Thread(target=self.__ApproveParticipationRequest, args=(client_id,))
            approval_thread[client_id].setDaemon(True)
            approval_thread[client_id].start()

        client_id = 0
        while (not self.__client_item_list[0]["approval"]) or (not self.__client_item_list[1]["approval"]):
            if self.__client_item_list[client_id]["approval"] and (not self.__client_item_list[client_id]["stream"].is_write_if_active):
                
                self.__state = GobbGameServerUnit.__RESULT
                
                for client_id in range(0, 2):
                    self.__client_item_list[client_id]["stream"].DeactivateReadIF()
                    
                for client_id in range(0, 2):
                    self.__client_item_list[client_id]["stream"].WaitWriteIFDeactivation()
                    self.__client_item_list[client_id]["stream"].Close()
                    self.__client_item_list[client_id]["stream"] = None

                self.__state = GobbGameServerUnit.__SLEEPING

                print("GAME SERVER UNIT [id=" + str(self.__game_server_id) + "] -> END")
                return

            client_id = (client_id + 1) % 2
            time.sleep(0.1)            
            

        print("GAME SERVER UNIT [id=" + str(self.__game_server_id) + "] -> EACH APPROVAL DONE")

        #ゲームの初期化
        self.__game = Gobb()
        self.__turn_player = self.__game.Initialize()
        self.__turn_client = self.__turn_player - 1
        self.__InstructGameStart(self.__turn_player)

        print("GAME SERVER UNIT [id=" + str(self.__game_server_id) + "] -> GAME START")

        #ゲーム開始
        self.__state = GobbGameServerUnit.__WAITING_PIECE_SELECTION
        for client_id in range(0, 2):
            self.__client_item_list[client_id]["thread"] = threading.Thread(target=self.__ControlClient, args=(client_id,))
            self.__client_item_list[client_id]["thread"].setDaemon(True)
            self.__client_item_list[client_id]["thread"].start()
        

        client_id = 0
        while self.__state != GobbGameServerUnit.__RESULT:
            
            if not self.__client_item_list[client_id]["stream"].is_write_if_active:
                self.__ReturnServerError((client_id + 1) % 2, GobbGameServerUnit.__OTHER_CLIENT_DISCONNECTED)
                self.__InstructGameEnd(GobbGameServerUnit.__ERROR_END)
                self.__state = GobbGameServerUnit.__RESULT
                break

            client_id = (client_id + 1) % 2
            time.sleep(0.001)

        print("GAME SERVER UNIT [id=" + str(self.__game_server_id) + "] -> GAME END")

        for client_id in range(0, 2):
            self.__client_item_list[client_id]["stream"].DeactivateReadIF()
            
        for client_id in range(0, 2):
            self.__client_item_list[client_id]["stream"].WaitWriteIFDeactivation()
            self.__client_item_list[client_id]["stream"].Close()
            self.__client_item_list[client_id]["stream"] = None

        self.__state = GobbGameServerUnit.__SLEEPING

        print("GAME SERVER UNIT [id=" + str(self.__game_server_id) + "] -> END")

     
    def __ApproveParticipationRequest(self, client_id:int) -> None:

        self.__client_item_list[client_id]["stream"].WaitWriteIFActivation()
        self.__client_item_list[client_id]["stream"].ActivateReadIF()

        buffer = bytearray(1024)

        r = self.__client_item_list[client_id]["stream"].Receive(buffer)

        if buffer[0] == GobbGameServerUnit.__REQUEST_PARTICIPATION_APPROVAL:
            name_length = int(buffer[1])
            self.__client_item_list[client_id]["name"] = buffer[2:2 + name_length]
            self.__ResponseParticipationApproval(client_id, client_id + 1)
            self.__client_item_list[client_id]["approval"] = True
            print("GAME SERVER UNIT [id=" + str(self.__game_server_id) + "] -> APPROVAL DONE [client-id=" + str(client_id) + "]")


    def __ControlClient(self, client_id:int) -> None:

        buffer = bytearray(4)
        while self.__state != GobbGameServerUnit.__RESULT:
    
            #受信
            r = self.__client_item_list[client_id]["stream"].Receive(buffer)

            #読み取ったデータの長さが0なら終了
            if r == 0:
                return

            #ターンを獲得していない場合はデータを破棄
            if not client_id == self.__turn_client:
                continue

            #クライアントエラーを受け取った場合
            if buffer[0] == GobbGameServerUnit.__ERROR_CLIENT:

                if buffer[1] == GobbGameServerUnit.__UNKNOWN_ERROR_CLIENT:
                    self.__ReturnServerError((client_id + 1) % 2, GobbGameServerUnit.__OTHER_CLIENT_DISCONNECTED)
                    self.__InstructGameEnd(GobbGameServerUnit.__ERROR_END)
                    self.__state = GobbGameServerUnit.__RESULT
                    break

                elif buffer[1] == GobbGameServerUnit.__CLIENT_DISCONNECTED:
                    self.__ReturnServerError((client_id + 1) % 2, GobbGameServerUnit.__OTHER_CLIENT_HAS_UNKNOWN_ERROR)
                    self.__InstructGameEnd(GobbGameServerUnit.__ERROR_END)
                    self.__state = GobbGameServerUnit.__RESULT
                    break

            #クライアントの駒選択待ちの状態で、クライアントが駒の選択を行った場合
            if self.__state == GobbGameServerUnit.__WAITING_PIECE_SELECTION and buffer[0] == GobbGameServerUnit.__REQUEST_COORDINATE_LIST:

                exposed_piece_id = None #駒を元あった場所から移動したことで露出した駒
                is_from_board = buffer[1] == 0xff or buffer[2] != 0xff

                piece_id = int(buffer[1]) #盤面から動かしたときの駒IDを取得する
                coordinate_from = [int((buffer[2] >> 4) & 0x0f), int(buffer[2] & 0x0f)] if is_from_board else None

                #盤上から選んだか
                if is_from_board:
                    
                    #座標が不正な値でないか
                    if self.__game.CheckPosition(coordinate_from):
                        self.__ReturnGameError(client_id, GobbGameServerUnit.__NON_EXISTENT_COORDINATE) #エラー：存在しない座標
                        continue

                    #座標に露出している駒を取得
                    piece_id = self.__game.GetPieceFromBoard(coordinate_from)
                    
                    #指定された座標にクライアントの駒があるか
                    if piece_id is None:
                        self.__ReturnGameError(client_id, GobbGameServerUnit.__NO_YOUR_PIECES_TO_SELECT) #エラー：指定された座標から移動可能なクライアントの駒がない
                        continue

                    #駒を選択
                    self.__game.SelectPiece(piece_id)

                    #動かしたい駒の上に駒がないか
                    if not self.__game.CanMove(coordinate_from):
                        self.__ReturnGameError(client_id, GobbGameServerUnit.__NO_YOUR_PIECES_TO_SELECT) #エラー：指定された座標から移動可能なクライアントの駒がない
                        continue
                    
                    #駒を盤上から削除
                    exposed_piece_id = self.__game.ErasePieceFromBoard(coordinate_from)

                else:

                    #駒を選択
                    self.__game.SelectPiece(piece_id)
                    
                    #駒IDが存在する値であるか
                    if not self.__game.CheckHandID():
                        self.__ReturnGameError(client_id, GobbGameServerUnit.__NON_EXISTENT_PIECE_ID) #エラー：存在しない駒ID
                        continue
                    
                    #選択された駒が本当に手札にあるか
                    if not self.__game.IsPieceInHand():
                        self.__ReturnGameError(client_id, GobbGameServerUnit.__PIECE_NOT_IN_YOUR_HAND) #エラー：手札に選択された駒がない
                        continue
                
                    #駒を手札から削除
                    self.__game.ErasePieceFromHand()
                

                #駒を配置可能な座標リスト
                coordinate_list = self.__game.CanPut(coordinate_from)


                #置ける座標があるか
                if len(coordinate_list) == 0:            
                    self.__ReturnGameError(client_id, GobbGameServerUnit.__NO_COORDINATES_TO_SELECT) #エラー：置ける座標がない
                    if is_from_board:
                        self.__game.UndoErasingPieceFromBoard(coordinate_from) #盤上から消した駒を元に戻す
                    else:
                        self.__game.UndoErasingPieceFromHand() #手札から消した駒を元に戻す
                    continue


                #駒取り上げ命令
                if is_from_board:
                    self.__InstructPiecePicking(None, exposed_piece_id, coordinate_from)
                else:
                    self.__InstructPiecePicking(piece_id, 0, None)

                #勝敗チェック
                if self.__game.JudgeResult(): #駒を持ち上げたことで決着した場合
                    self.__InstructGameEnd(GobbGameServerUnit.__LOSE, (self.__turn_player % 2) + 1) #決着
                    self.__state = GobbGameServerUnit.__RESULT
                    break
                else:
                    self.__ResponseCoordinateList(client_id, coordinate_list) #配置場所選択待ち状態へ
                    self.__state = GobbGameServerUnit.__WAITING_PIECE_PLACEMENT
                

            #クライアントの配置場所選択待ちの状態で、クライアントが駒の配置を行った場合
            elif self.__state == GobbGameServerUnit.__WAITING_PIECE_PLACEMENT and buffer[0] == GobbGameServerUnit.__REQUEST_PIECE_PLACEMENT:

                coordinate_to = [int((buffer[1] >> 4) & 0x0f), int(buffer[1] & 0x0f)]

                #座標が不正な値でないか
                if self.__game.CheckPosition(coordinate_to):
                    self.__ReturnGameError(client_id, GobbGameServerUnit.__NON_EXISTENT_COORDINATE) #エラー：存在しない座標
                    continue

                #配置可能な座標が選択されたか
                if not self.__game.PutPiece(coordinate_to):
                    self.__ReturnGameError(client_id, GobbGameServerUnit.__CANNOT_PLACE_PIECE) #エラー：指定された座標に駒を置けない
                    continue

                #駒配置
                self.__InstructPiecePlacement(piece_id, coordinate_to)

                #勝敗チェック
                if self.__game.JudgeResult():
                    self.__InstructGameEnd(GobbGameServerUnit.__WIN, self.__turn_player) #決着
                    self.__state = GobbGameServerUnit.__RESULT
                    break
                else:
                    self.__turn_player = self.__game.ChangeTurn() #ターン切り替え
                    self.__turn_client = self.__turn_player - 1
                    self.__state = GobbGameServerUnit.__WAITING_PIECE_SELECTION



    


    #0x10：RESPONSE_PARTICIPATION_APPROVAL
    #"RESPONSE_PARTICIPATION_APPROVAL"は、"REQUEST_PARTICIPATION_APPROVAL"を承認する場合の応答となるサーバからの通信です。
    
    def __ResponseParticipationApproval(self, client_id:int, player_id:int) -> None:

        buffer = bytearray(4)
        buffer[0] = GobbGameServerUnit.__RESPONSE_PARTICIPATION_APPROVAL
        buffer[1] = player_id
        buffer[2] = 0x00
        buffer[3] = 0x00

        self.__client_item_list[client_id]["stream"].Send(buffer)




    #0x11：RESPONSE_COORDINATE_LIST
    #"RESPONSE_COORDINATE_LIST"は、"REQUEST_COORDINATE_LIST"の応答として、クライアントが選択した駒を配置可能な座標のリストを返すサーバからの通信です。

    def __ResponseCoordinateList(self, client_id:int, coordinate_list:list) -> None:

        count = len(coordinate_list)
        
        buffer = bytearray(2 + count)
        buffer[0] = GobbGameServerUnit.__RESPONSE_COORDINATE_LIST
        buffer[1] = count
         
        for i in range(2, 2 + count):
            buffer[i] = ((coordinate_list[i - 2][0] << 4) & 0xf0) | (coordinate_list[i - 2][1] & 0x0f)

        buffer = buffer + bytearray(4 - ((2 + count) % 4))
        self.__client_item_list[client_id]["stream"].Send(buffer)





    #0x12：INSTRUCT_GAME_START
    #"INSTRUCT_GAME_START"は、ゲームに参加する全てのクライアントに対してゲームの開始命令を行うサーバからの通信です。

    def __InstructGameStart(self, first_turn_player_id:int) -> None:

        for client_id in range(0, 2):
            
            other_player = (client_id + 1) % 2
            other_player_name_length = len(self.__client_item_list[other_player]["name"])

            buffer = bytearray(3)
            buffer[0] = GobbGameServerUnit.__INSTRUCT_GAME_START
            buffer[1] = first_turn_player_id
            buffer[2] = other_player_name_length

            buffer = buffer + self.__client_item_list[other_player]["name"]
            buffer = buffer + bytearray(4 - ((3 + other_player_name_length) % 4))

            self.__client_item_list[client_id]["stream"].Send(buffer)





    #0x13：INSTRUCT_PIECE_PICKING
    #"#NSTRUCT_PIECE_PICKING"は、正常な"REQUEST_COORDINATE_LIST"によって指定された駒を手札または盤上から取り除くよう、
    #ゲームに参加する全てのクライアントに対して命令を行うサーバからの通信です。

    def __InstructPiecePicking(self, hand_piece_id:int, exposed_piece_id:int, coordinate:list) -> None:

        buffer = bytearray(4)

        buffer[0] = GobbGameServerUnit.__INSTRUCT_PIECE_PICKING
        buffer[1] = hand_piece_id if hand_piece_id is not None else 0xff
        buffer[2] = exposed_piece_id if exposed_piece_id != 0 else 0xff
        buffer[3] = ((coordinate[0] << 4) & 0xf0) | (coordinate[1] & 0x0f) if coordinate is not None else 0xff

        for client_id in range(0, 2):
            self.__client_item_list[client_id]["stream"].Send(buffer)





    #0x14：INSTRUCT_PIECE_PLACEMENT
    #"INSTRUCT_PIECE_PLACEMENT"は、正常な"REQUEST_PIECE_PLACEMENT"によって指定された駒を指定された座標に配置するよう、
    #ゲームに参加する全てのクライアントに対して命令を行うサーバからの通信です。

    def __InstructPiecePlacement(self, piece_id:int, coordinate_to:list) -> None:

        buffer = bytearray(4)

        buffer[0] = GobbGameServerUnit.__INSTRUCT_PIECE_PLACEMENT
        buffer[1] = piece_id
        buffer[2] = ((coordinate_to[0] << 4) & 0xf0) | (coordinate_to[1] & 0x0f)
        buffer[3] = 0x00

        for client_id in range(0, 2):
            self.__client_item_list[client_id]["stream"].Send(buffer)




    #0x15：INSTRUCT_GAME_END
    #"INSTRUCT_GAME_END"は、ゲームに参加する全てのクライアントに対してゲームの終了命令を行うサーバからの通信です。

    def __InstructGameEnd(self, end_type:int, winner_player_id:int = 0xff) -> bool:

        buffer = bytearray(4)
        buffer[0] = GobbGameServerUnit.__INSTRUCT_GAME_END
        buffer[1] = end_type
        buffer[2] = 0x00
        buffer[3] = 0x00

        if (end_type == GobbGameServerUnit.__WIN or end_type == GobbGameServerUnit.__LOSE) and (winner_player_id - 1) < 2:        

            for client_id in range(0, 2):
                buffer[1] = GobbGameServerUnit.__WIN if client_id == (winner_player_id - 1) else GobbGameServerUnit.__LOSE
                self.__client_item_list[client_id]["stream"].Send(buffer)
            return True
        
        elif end_type == GobbGameServerUnit.__ERROR_END:
            
            for client_id in range(0, 2):
                self.__client_item_list[client_id]["stream"].Send(buffer)
            return True

        return False





    #0x16：ERROR_GAME
    #"ERROR_GAME"は、クライアントからの送信データがゲーム状況からして不正であると判断できるため、
    #ターンの最初からやり直しを求めるエラー通知をクライアントへ行うサーバからの通信です。

    def __ReturnGameError(self, client_id:int, error_type:int) -> bool:
        
        allow_error_type = [
            GobbGameServerUnit.__NON_EXISTENT_PIECE_ID,
            GobbGameServerUnit.__NON_EXISTENT_COORDINATE,
            GobbGameServerUnit.__PIECE_NOT_IN_YOUR_HAND,
            GobbGameServerUnit.__NO_YOUR_PIECES_TO_SELECT,
            GobbGameServerUnit.__NO_COORDINATES_TO_SELECT,
            GobbGameServerUnit.__CANNOT_PLACE_PIECE,
        ]
        
        if error_type not in allow_error_type:
            return False
        
        buffer = bytearray(4)
        buffer[0] = GobbGameServerUnit.__ERROR_GAME
        buffer[1] = error_type
        buffer[2] = 0x00
        buffer[3] = 0x00

        self.__client_item_list[client_id]["stream"].Send(buffer)
        return True




    #0x17：ERROR_SERVER
    #"ERROR_SERVER"は、サーバ側で発生したエラーについてクライアントへ通知を行うサーバからの通信です。

    def __ReturnServerError(self, client_id:int, error_type:int) -> bool:

        allow_error_type = [
            GobbGameServerUnit.__UNKNOWN_ERROR_SERVER,
            GobbGameServerUnit.__OTHER_CLIENT_HAS_UNKNOWN_ERROR,
            GobbGameServerUnit.__OTHER_CLIENT_DISCONNECTED
        ]
        
        if error_type not in allow_error_type:
            return False

        buffer = bytearray(4)
        buffer[0] = GobbGameServerUnit.__ERROR_SERVER
        buffer[1] = error_type
        buffer[2] = 0x00
        buffer[3] = 0x00

        self.__client_item_list[client_id]["stream"].Send(buffer)
        return True
