/* ---------- グローバル ---------- */

//element
let el_frame = document.getElementById("frame");
let el_game_message;
let el_squares;
let el_hand_num;

//game
let turn_player_id;
let is_placed;
let hand_num;

//player
let my_player_id;
let my_player_name = arg;
let other_player_id;
var other_player_name;


//ファイルパス
const PIECE_IMG_PATH = [
    ["img/white_small.png", "img/white_middle.png", "img/white_large.png"], //player_id == 1
    ["img/black_small.png", "img/black_middle.png", "img/black_large.png"]  //player_id == 2
];

const SQUARE_IMG_PATH = "img/square.jpg";



/* ---------- クライアントきっかけの処理（クライアントが画面を操作する事で発生する処理） ---------- */

//盤上からコマを選択
function SelectPieceFromBoard(row, colmun){
    if(is_placed[row, colmun]){
        RequestCoordinateList(0xff, [row, colmun]);
    }
}

//手札からコマを選択
function SelectPieceFromHand(piece_size){
    let piece_id = ((my_player_id << 4) & 0xf0) | (piece_size & 0x0f);
    RequestCoordinateList(piece_id);
}

//コマを置くマスを選択する
function SelectCoordinate(row, colmun){
    RequestPiecePlacement([row, colmun]);
}





/* ---------- サーバきっかけの処理（サーバから特定の通信を受け取ったときに発生する処理）---------- */

//マッチングを開始する
function WaitGameStart(player_id){
    my_player_id = player_id;
    other_player_id = (my_player_id % 2) + 1;
    el_frame.innerHTML = '<div class="middle">マッチング中：対戦相手が現れるのを待っています。</div>';
}

//ゲーム開始処理 & ゲーム画面生成
//initial()
function StartGame(first_turn_player_id, other_player_name){

    window.other_player_name = other_player_name; 
    //this.hogeとするとローカル変数のhogeを呼んでいるような気もしなくはないかもしれませんが、this.hogeとローカル変数のhogeは別物です。この場合、this.hogeは作成すらされていないので、『undefined』となります。
    //なお、letやconstをトップレベルで宣言した場合、グローバルスコープにはなるのですが、windowオブジェクトのプロパティには追加されません。

    //Begin: 外枠となる要素の生成
    el_frame.innerHTML = "";
    el_frame.insertAdjacentHTML("beforeend", '<div id="game_message_line"><div id="game_message"></div></div>');
    el_frame.insertAdjacentHTML("beforeend", '<div id="main_line"><div id="other_player_hand"></div><div id="board"></div><div id="my_hand"></div></div>');
    //End
    
    //Begin: メッセージ表示行の生成
    turn_player_id = first_turn_player_id;
    el_game_message = document.getElementById("game_message");
    DisplayMessage(turn_player_id == my_player_id ?  my_player_name + "（あなた）のターンです" : other_player_name +  "（相手）のターンです");
    //End
    
    //Begin: 盤生成
    let el_board = document.getElementById("board");
    
    for(let i = 0; i < 3; i++){
        for(let j = 0; j < 3; j++){
            let id = "square" + i + "" + j;
            let args = i + ", " + j;
            let square = '<div id="' + id + '" class="square" onclick="SelectPieceFromBoard(' + args + '); SelectCoordinate(' + args + ')"></div>';
            el_board.insertAdjacentHTML("beforeend", square + (i != 2 && j == 2 ? "<br>" : ""));
        }
    }

    el_squares = [
        [document.getElementById("square00"), document.getElementById("square01"), document.getElementById("square02")],
        [document.getElementById("square10"), document.getElementById("square11"), document.getElementById("square12")],
        [document.getElementById("square20"), document.getElementById("square21"), document.getElementById("square22")]
    ];

    for (const value_row of el_squares) {
        for (const value_column of value_row) {
            value_column.innerHTML = "<img src='img/" + SQUARE_IMG_PATH + "' class='square_img'>";
        }
    }

    is_placed = [
        [false, false, false],
        [false, false, false],
        [false, false, false]
    ];
    //End
    
    //Begin: 自分の手札置場の生成
    let el_my_hand = document.getElementById("my_hand");
    el_my_hand.insertAdjacentHTML("beforeend", '<span class="player_name">&lt;あなた&gt;<br>' + my_player_name + '</span>');
    for(let i = 0; i < 3; i++){
        let img = '<img src="' + PIECE_IMG_PATH[my_player_id - 1][i] + '" onclick="SelectPieceFromHand(' + (i + 1) + ')" class="hand_img">';
        let num = '<div id="hand_num_' + ((my_player_id * 10) + (i + 1)) + '" class="hand_num"></div>';
        el_my_hand.insertAdjacentHTML("beforeend", img + num);
    }
    //End

    //Begin: 相手の手札置場の生成
    let el_other_player_hand = document.getElementById("other_player_hand");
    el_other_player_hand.insertAdjacentHTML("beforeend", '<span class="player_name">&lt;相手&gt;<br>' + other_player_name + '</span>');
    let other_player_id = (my_player_id % 2) + 1;
    for(let i = 0; i < 3; i++){
        let img = '<img src="' + PIECE_IMG_PATH[other_player_id - 1][i] + '" class="hand_img">';
        let num = '<div id="hand_num_' + ((other_player_id * 10) + (i + 1)) + '" class="hand_num"></div>';
        el_other_player_hand.insertAdjacentHTML("beforeend", img + num);
    }
    //End
    
    //Begin: 手札の残数設定
    el_hand_num = [
        [document.getElementById("hand_num_11"), document.getElementById("hand_num_12"), document.getElementById("hand_num_13")], //player_id == 1
        [document.getElementById("hand_num_21"), document.getElementById("hand_num_22"), document.getElementById("hand_num_23")]  //player_id == 2
    ];
    
    hand_num = [
        [2, 2, 2], //player_id == 1
        [2, 2, 2]  //player_id == 2
    ];

    for(let i = 0; i < 2; i++){
        for(let j = 0; j < 3; j++){
            el_hand_num[i][j].innerHTML = "x" + hand_num[i][j];
        }
    }
    //End

    return turn_player_id == my_player_id; //先攻であるか
}

//選択した駒を、手札置場から消す
function ErasePieceFromHand(player_id, piece_size){
    hand_num[player_id - 1][piece_size - 1]--;
    el_hand_num[player_id - 1][piece_size - 1].innerHTML = "x" + hand_num[player_id - 1][piece_size - 1];
}

//選択した駒を置けるマスに色を付ける
//CanPutPlaced(can_put_placed)
function IndicatePlaceablePosition(can_put_placed) {
    for (const value of can_put_placed) {
        el_squares[value[0]][value[1]].classList.add('cover');
    }
}

//色を付けたマスを元に戻す
function StopIndicatingPlaceablePosition(){
    for(let i = 0; i < 3; i++){
        for(let j = 0; j < 3; j++){
            el_squares[i][j].classList.remove('cover');
        }
    }
}

//空のコマを配置する（=マスを空にする）
function PlaceEmpty(position){
    el_squares[position[0]][position[1]].innerHTML = "<img src='" + SQUARE_IMG_PATH + "' class='square_img'>";
}

//コマを配置する
//PutPlaced()
function PlacePiece(player, piece_size, position) {

    StopIndicatingPlaceablePosition();
    el_squares[position[0]][position[1]].innerHTML = '<img src="' + PIECE_IMG_PATH[player - 1][piece_size - 1] + '" class="square_img">';

    /*
    if (player == 1) {
        if (piece_size == 1) {
            el_squares[position[0]][position[1]].innerHTML = "<img src='img/test_b1.jpg' width='200px''>";
        } else if (piece_size == 2) {
            el_squares[position[0]][position[1]].innerHTML = "<img src='img/test_b2.jpg' width='200px'>";
        } else if (piece_size == 3) { 
            el_squares[position[0]][position[1]].innerHTML = "<img src='img/test_b3.jpg' width='200px''>";
        } 
    } else if (player == 2) {
        if (piece_size == 1) {
            el_squares[position[0]][position[1]].innerHTML = "<img src='img/test_o1.jpg' width='200px'>";
        } else if (piece_size == 2) {
            el_squares[position[0]][position[1]].innerHTML = "<img src='img/test_o2.jpg' width='200px'>";
        } else if (piece_size == 3) { 
            el_squares[position[0]][position[1]].innerHTML = "<img src='img/test_o3.jpg' width='200px'>";
        }
    }*/
}


//ターンを切り替える
function ChangeTurn(){
    turn_player_id = (turn_player_id % 2) + 1;
    DisplayMessage(turn_player_id == my_player_id ?  my_player_name + "（あなた）のターンです" : other_player_name +  "（相手）のターンです");
    return turn_player_id == my_player_id;
}

//ゲームを終了する
function EndGame(end_message){
    turn_player_id = 0xff;
    DisplayMessage(end_message);
}

//ゲームのメッセージを表示する
function DisplayMessage(game_message){
    el_game_message.innerHTML = game_message;
}

//エラーを表示する
function DisplayError(error_message){
    alert(error_message);
}
