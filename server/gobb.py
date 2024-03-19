import random

class Gobb:

    # 手札定数
    __const_handID = [0x11, 0x12, 0x13, 0x21, 0x22, 0x23]

    # 座標定数
    __const_pos = [0x00, 0x01, 0x02, 0x10, 0x11, 0x12, 0x20, 0x21, 0x22]

    # init
    def __init__(self) -> None:

        # インスタンス変数はメソッド内で定義（一般的には__init_内）
        
        # 手札
        self.hand = [
            [0x11, 0x11, 0x12, 0x12, 0x13, 0x13],
            [0x21, 0x21, 0x22, 0x22, 0x23, 0x23]
        ]
        # 上から見た盤面
        self.board = {
            # column: suf, row: j
            0: {0: 0, 1: 0, 2: 0},
            1: {0: 0, 1: 0, 2: 0},
            2: {0: 0, 1: 0, 2: 0},
        }
        # 盤面の立体版
        self.soild_board = {
            1: { # 小駒
                0: {0: 0, 1: 0, 2: 0},
                1: {0: 0, 1: 0, 2: 0},
                2: {0: 0, 1: 0, 2: 0},
            },
            2: {
                0: {0: 0, 1: 0, 2: 0},
                1: {0: 0, 1: 0, 2: 0},
                2: {0: 0, 1: 0, 2: 0},
            },
            3: { # 大駒
                0: {0: 0, 1: 0, 2: 0},
                1: {0: 0, 1: 0, 2: 0},
                2: {0: 0, 1: 0, 2: 0},
            },
        }

        # ターンを持っているプレイヤー
        self.__turn_player_id = 0xff

        # 選択されている駒
        self.__selected_handID = 0xff

        # 現在のdisp_board 
        self.__current_disp_board = []



    # 初期化処理
    def Initialize(self):
        self.__turn_player_id = random.randint(1, 2) # 先攻の決定
        return self.__turn_player_id

    # 指定した座標の一番表面にある駒のIDを取得する
    def GetPieceFromBoard(self, pos):
        return self.board[pos[0]][pos[1]] if self.__turn_player_id == (self.board[pos[0]][pos[1]] >> 4) & 0x0f else None

    # 駒を選択する
    def SelectPiece(self, handID):
        self.__selected_handID = handID

    # 駒IDが不正な値でないかチェックする
    def CheckHandID(self):
        return self.__selected_handID in self.__const_handID     

    # 座標が不正な値でないかチェックする
    def CheckPosition(self, pos):
        return pos in self.__const_pos

    # 駒が手札にあるか
    def IsPieceInHand(self):
        return self.__selected_handID in self.hand[self.__turn_player_id - 1]

    # 選択された駒の上に駒がないか
    def CanMove(self, pos):
        return self.__selected_handID == self.board[pos[0]][pos[1]]

    # 駒を盤上から消す
    def ErasePieceFromBoard(self, pos):

        # pos: [i, j]  盤面の駒が選択された座標
        piece_size = self.__selected_handID & 0x0f
        self.soild_board[piece_size][pos[0]][pos[1]] = 0 # 元居た座標から消す
        # 動かしたコマの大きさで分岐
        if piece_size == 3:
            # 下に駒はあるか
            if self.soild_board[piece_size - 1][pos[0]][pos[1]]:
                # あるなら画面に表示する
                self.board[pos[0]][pos[1]] = self.soild_board[piece_size - 1][pos[0]][pos[1]]
            elif self.soild_board[piece_size - 2][pos[0]][pos[1]]:
                self.board[pos[0]][pos[1]] = self.soild_board[piece_size - 2][pos[0]][pos[1]]
            else :
                self.board[pos[0]][pos[1]] = 0
        elif piece_size == 2:
            if self.soild_board[piece_size - 1][pos[0]][pos[1]]:
                self.board[pos[0]][pos[1]] = self.soild_board[piece_size - 1][pos[0]][pos[1]]
            else :
                self.board[pos[0]][pos[1]] = 0
        else :
            self.board[pos[0]][pos[1]] = 0

        return self.board[pos[0]][pos[1]] # 駒の下に隠れていた駒のID


    # 駒を盤上へ戻す
    def UndoErasingPieceFromBoard(self, pos):
        piece_size = self.__selected_handID & 0x0f
        self.soild_board[piece_size][pos[0]][pos[1]] = self.__selected_handID
        self.board[pos[0]][pos[1]] = self.__selected_handID 

    # 駒を手札から消す
    def ErasePieceFromHand(self):
        self.hand[self.__turn_player_id - 1].remove(self.__selected_handID)

    # 駒を手札へ戻す
    def UndoErasingPieceFromHand(self):
        self.hand[self.__turn_player_id - 1].append(self.__selected_handID)
        
    # 勝敗がついたかの判断
    def JudgeResult(self):
        
        '''盤面の勝利条件
        0: {0: 0, 1: 0, 2: 0}, | 横[0,1,2], [3,4,5], [6,7,8]
        1: {3: 0, 4: 0, 5: 0}, | 縦[0,3,6], [1,4,7], [2,5,8]
        2: {6: 0, 7: 0, 8: 0}, | 斜[0,4,8], [2,4,6]'''

        # 盤面上の駒を1pと2pに分けて一つの配列に
        result_list = []
        for piece_i in self.board:
            for piece_j in self.board[piece_i]:
                piece_player = self.board[piece_i][piece_j] & 0xf0
                result_list.append(piece_player)
        # 横
        suf = 0
        for beside in range(3):
            align_list = []
            for num in result_list[suf:suf + 3]:
                align_list.append(num)
            suf += 3
            if self.IsAligned(align_list):
                return True
        # 縦
        suf = 0
        for vertical in range(3):
            align_list = []
            for num in result_list[suf:suf + 7:3]:
                align_list.append(num)
            suf += 1
            if self.IsAligned(align_list):
                return True
        # 斜め
        align_list = [result_list[0], result_list[4], result_list[8]]
        if self.IsAligned(align_list):
            return True
        align_list = [result_list[2], result_list[4], result_list[6]]
        if self.IsAligned(align_list):
            return True
        
        return False

    # 揃っているか
    def IsAligned(self, align_list):
        return align_list[0] == align_list[1] == align_list[2] != 0


    # 置けるマスのリストを返す
    def CanPut(self, pos):
        if pos == None:
            pos = [9, 9]
        disp_board = [] # 置ける場所を表す盤面のリスト
        piece_size = self.__selected_handID & 0x0f
        # 自分以上の大きさの駒のある場所以外
        for piece_i in self.board:
            for piece_j in self.board[piece_i]:
                if self.board[piece_i][piece_j] & 0x0f < piece_size:
                    # 同じ場所には置けない
                    if piece_i != pos[0] or piece_j != pos[1]:
                        # 置ける場所の座標を追記
                        disp_board.append([piece_i, piece_j])

        self.__current_disp_board = disp_board
        return self.__current_disp_board


    # 駒を置く
    def PutPiece(self, pos):

        # 置ける位置を選択したかの分岐
        if pos in self.__current_disp_board:
            piece_size = self.__selected_handID & 0x0f
            self.soild_board[piece_size][pos[0]][pos[1]] = self.__selected_handID
            self.board[pos[0]][pos[1]] = self.__selected_handID # 置いた駒の盤面表示用
            
            self.__selected_handID = 0xff
            self.__current_disp_board = []
            return True

        return False


    # ターン切り替え
    def ChangeTurn(self):
        self.__turn_player_id = (self.__turn_player_id % 2) + 1
        return self.__turn_player_id