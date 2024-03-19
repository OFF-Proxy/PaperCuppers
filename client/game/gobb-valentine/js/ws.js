/*-------------------- global --------------------*/ //グローバル変数はwindowオブジェクトのプロパティらしい

//WebSocket
//開発用let ws = new WebSocket("ws://127.0.0.1:8000/");
console.log("ROADED ws.js");
let ws = new WebSocket("ws://127.0.0.1:8000/");

//control flag
let allow_request_participation_approval = false;
let allow_request_coordinate_list = false;
let allow_request_piece_placement = false;
let allow_inform_error_client = false;



/*-------------------- const --------------------*/

//アプリケーションデータタイプ
const REQUEST_PARTICIPATION_APPROVAL = 0x00;
const REQUEST_COORDINATE_LIST = 0x01;
const REQUEST_PIECE_PLACEMENT = 0x02;
const ERROR_CLIENT = 0x03;
const RESPONSE_PARTICIPATION_APPROVAL = 0x10;
const RESPONSE_COORDINATE_LIST = 0x11;
const INSTRUCT_GAME_START = 0x12;
const INSTRUCT_PIECE_PICKING = 0x13
const INSTRUCT_PIECE_PLACEMENT = 0x14;
const INSTRUCT_GAME_END = 0x15;
const ERROR_GAME = 0x16;
const ERROR_SERVER = 0x17;

//エラータイプ
const UNKNOWN_ERROR_CLIENT = 0x00;
const CLIENT_DISCONNECTED = 0x01;
const UNKNOWN_ERROR_SERVER = 0x10;
const OTHER_CLIENT_HAS_UNKNOWN_ERROR = 0x11;
const OTHER_CLIENT_DISCONNECTED = 0x12;
const NON_EXISTENT_PIECE_ID = 0x20;
const NON_EXISTENT_COORDINATE = 0x21;
const PIECE_NOT_IN_YOUR_HAND = 0x22;
const NO_YOUR_PIECES_TO_SELECT = 0x23;
const NO_COORDINATES_TO_SELECT = 0x24;
const CANNOT_PLACE_PIECE = 0x25; 

//エンドタイプ
const WIN_END = 0x00;
const LOSE_END = 0x01;
const ERROR_END = 0x02;


//エラータイプごとのメッセージ
const ERROR_MESSAGE = {
        
    [UNKNOWN_ERROR_SERVER]: "ゲーム中止エラー：サーバで原因不明のエラーが発生しました。ゲームを終了します。",
    [OTHER_CLIENT_HAS_UNKNOWN_ERROR]: "ゲーム中止エラー：対戦相手側で原因不明のエラーが発生しました。ゲームを終了します。",
    [OTHER_CLIENT_DISCONNECTED]: "ゲーム中止エラー：対戦相手が通信を切断しました。ゲームを終了します。",

    [NON_EXISTENT_PIECE_ID]: "エラー：存在しない駒が選択されました。駒を再選択して下さい。",
    [NON_EXISTENT_COORDINATE]: "エラー：存在しないマスが選択されました。マスを再選択して下さい。",
    [PIECE_NOT_IN_YOUR_HAND]: "エラー：その駒は手札にありません。駒を再選択して下さい。",
    [NO_YOUR_PIECES_TO_SELECT]: "エラー：そのマスには移動可能なあなたの駒がありません。駒を再選択して下さい。",
    [NO_COORDINATES_TO_SELECT]: "エラー：配置可能なマスが見つかりませんでした。駒を再選択して下さい。",
    [CANNOT_PLACE_PIECE]: "エラー：選択中の駒を配置できないマスです。マスを再選択して下さい。"

};

//ゲーム終了時のメッセージ
const END_MESSAGE = {
    [WIN_END]: "あたなの勝ちです。",
    [LOSE_END]: "あなたの負けです。",
    [ERROR_END]: "エラーによってゲームが終了しました。"
}





/*-------------------- sender --------------------*/

//send
async function WsSend(data){
    ws.send(data);
}

//close
async function WsClose(){
    ws.close(1000);
}

//0x00：REQUEST_PARTICIPATION_APPROVAL
//"REQUEST_PARTICIPATION_APPROVAL"は、クライアントが自らのゲームへの参加を承認するよう、サーバへと要求を行うクライアントからの通信です。

function RequestParticipationApproval(name){

    if(!allow_request_participation_approval){
        return;
    }

    let encoder = new TextEncoder();
    let name_byte_array = encoder.encode(name); //Uint8Arry UTF-8
    
    let buffer = new ArrayBuffer(2 + name_byte_array.length + (4 - ((2 + name_byte_array.length) % 4)));
    let byte_array = new DataView(buffer);
    byte_array.setUint8(0, REQUEST_PARTICIPATION_APPROVAL);
    byte_array.setUint8(1, name_byte_array.length);
    
    for(let i = 0; i < name_byte_array.length; i++){
        byte_array.setUint8(2 + i, name_byte_array[i])
    }
    
    WsSend(buffer);
}



//0x01：REQUEST_COORDINATE_LIST
//"REQUEST_COORDINATE_LIST"は、選択したコマを配置可能な座標のリストを返却するよう、サーバへと要求を行うクライアントからの通信です。

function RequestCoordinateList(piece_id, coordinate_from = [0xff, 0xff]){

    if(!allow_request_coordinate_list){
        return;
    }

    let buffer = new ArrayBuffer(4);
    let byte_array = new DataView(buffer);
    byte_array.setUint8(0, REQUEST_COORDINATE_LIST);
    byte_array.setUint8(1, piece_id);
    byte_array.setUint8(2, ((coordinate_from[0] << 4) & 0xf0) | (coordinate_from[1] & 0x0f));

    WsSend(byte_array);
}


//0x02：REQUEST_PIECE_PLACEMENT
//"REQUEST_PIECE_PLACEMENT"は、選択した座標に駒を配置するよう、サーバへと要求を行うクライアントからの通信です。

function RequestPiecePlacement(coordinate_to){

    if(!allow_request_piece_placement){
        return;
    }

    let buffer = new ArrayBuffer(4);
    let byte_array = new DataView(buffer);
    byte_array.setUint8(0, REQUEST_PIECE_PLACEMENT);
    byte_array.setUint8(1, ((coordinate_to[0] << 4) & 0xf0) | (coordinate_to[1] & 0x0f));

    WsSend(byte_array);
}


//0x03：ERROR_CLIENT
//"ERROR_CLIENT"は、クライアント側で発生したエラーについてサーバへ通知を行うクライアントからの通信です。

function InformErrorClient(error_type){

    if(!allow_inform_error_client){
        return;
    }

    let buffer = new ArrayBuffer(4);
    let byte_array = new DataView(buffer);
    byte_array.setUint8(0, ERROR_CLIENT);
    byte_array.setUint8(1, error_type);

    WsSend(byte_array);
}





/*-------------------- receiver --------------------*/

function OnOpen(event){ //function(event){} MessageEvent
    console.log("connected.");
    allow_inform_error_client = true;
    allow_request_participation_approval = true;
    RequestParticipationApproval(arg);
    WaitParticipationApproval();
}

async function OnMessage(event){

    let blob = event.data;
    let buffer = await blob.arrayBuffer();
    let byte_array = new DataView(buffer);

    let offset = 0;
    while(offset < byte_array.byteLength){

        let type = byte_array.getUint8(offset);
        
        if(type == RESPONSE_PARTICIPATION_APPROVAL){

            my_player_id = byte_array.getUint8(offset + 1);
            WaitGameStart(my_player_id);
            allow_request_participation_approval = false;
            offset = offset + 4;
            
        }else if(type == RESPONSE_COORDINATE_LIST){
        
            let count = byte_array.getUint8(offset + 1);
            let coordinate_list = [];
            for(let i = 0; i < count; i++){
                let tmp = byte_array.getUint8(offset + 2 + i);
                coordinate_list[i] = [(tmp >> 4) & 0x0f, tmp & 0x0f];
            }
    
            IndicatePlaceablePosition(coordinate_list);
            
            allow_request_coordinate_list = false;
            allow_request_piece_placement = true;
            offset = offset + 2 + count + (4 - ((2 + count) % 4));
    
        }else if(type == INSTRUCT_GAME_START){
    
            let first_turn_player_id = byte_array.getUint8(offset + 1);
            let other_player_name_length = byte_array.getUint8(offset + 2);
            let other_player_name_byte_array = new Uint8Array(other_player_name_length);
            for(let i = 0; i < other_player_name_length; i++){
                other_player_name_byte_array[i] = byte_array.getUint8(offset + 3 + i);
            }
            let decorder = new TextDecoder();
            let other_player_name = decorder.decode(other_player_name_byte_array);
    
            let have_turn = StartGame(first_turn_player_id, other_player_name);

            allow_request_coordinate_list = have_turn;
            offset = offset + 3 + other_player_name_length + (4 - ((3 + other_player_name_length) % 4));
        
        }else if(type == INSTRUCT_PIECE_PICKING){
 
            let hand_piece_id = byte_array.getUint8(offset + 1);
            let exposed_piece_id = byte_array.getUint8(offset + 2);
            let tmp = byte_array.getUint8(offset + 3);
            let coordinate = [(tmp >> 4) & 0x0f, tmp & 0x0f];
    
            if(hand_piece_id == 0xff){
                if(exposed_piece_id == 0xff){
                    PlaceEmpty(coordinate);
                }else{
                    let plyer_id = (exposed_piece_id >> 4) & 0x0f;
                    let piece_size = exposed_piece_id & 0x0f;
                    PlacePiece(plyer_id, piece_size, coordinate);
                }
            }else{
                let plyer_id = (hand_piece_id >> 4) & 0x0f;
                let piece_size = hand_piece_id & 0x0f;
                ErasePieceFromHand(plyer_id, piece_size);
            }

            offset = offset + 4;
    
        }else if(type == INSTRUCT_PIECE_PLACEMENT){
    
            let piece_id = byte_array.getUint8(offset + 1);
            let tmp = byte_array.getUint8(offset + 2);
            let coordinate_to = [(tmp >> 4) & 0x0f, tmp & 0x0f];
    
            let plyer_id = (piece_id >> 4) & 0x0f;
            let piece_size = piece_id & 0x0f;
    
            PlacePiece(plyer_id, piece_size, coordinate_to);
            let have_turn = ChangeTurn();

            allow_request_piece_placement = false;
            allow_request_coordinate_list = have_turn;
            offset = offset + 4;
    
        }else if(type == INSTRUCT_GAME_END){
        
            let end_type = byte_array.getUint8(offset + 1);
            EndGame(END_MESSAGE[end_type]);

            allow_request_participation_approval = false;
            allow_request_coordinate_list = false;
            allow_request_piece_placement = false;
            allow_inform_error_client = false;
            offset = offset + 4;
            return;
    
        }else if(type == ERROR_GAME){
    
            let error_type = byte_array.getUint8(offset + 1);
            DisplayError(ERROR_MESSAGE[error_type]);
            if(error_type == UNKNOWN_ERROR_SERVER || error_type == OTHER_CLIENT_HAS_UNKNOWN_ERROR || error_type == OTHER_CLIENT_DISCONNECTED){
                DisplayMessage(ERROR_MESSAGE[error_type]);
            }
            offset = offset + 4;
    
        }else if(type == ERROR_SERVER){
    
            let error_type = byte_array.getUint8(offset + 1);
            DisplayError(ERROR_MESSAGE[error_type]);
            offset = offset + 4;
            return;

        }else{
    
            //通信タイプがサーバ宛通信であるものは破棄
            return;
        }

    }
}

function OnError(event){
    try{
        InformErrorClient(UNKNOWN_ERROR_CLIENT);
        WsClose();
    }catch{
        
    }
    console.log(event);
    DisplayError(ERROR_MESSAGE[UNKNOWN_ERROR_SERVER]);
    EndGame(END_MESSAGE[ERROR_END]);
}

ws.onopen = OnOpen;
ws.onmessage = OnMessage;
ws.onclose = function(event){ 
    console.log("disconnected.");
    console.log(event);
}

ws.onerror = OnError;

window.beforeunload = function(){ WsClose(); }