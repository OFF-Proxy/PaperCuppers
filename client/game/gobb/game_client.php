<?php $player_name = filter_input(INPUT_POST, "player_name"); ?>
<?php $flg = is_string($player_name); ?>
<?php $player_name = is_null($player_name) ? "" : $player_name; ?>
<!DOCTYPE html>
<html lang="ja">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <link rel="preconnect" href="https://fonts.googleapis.com">
        <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
        <link href="https://fonts.googleapis.com/css2?family=Dela+Gothic+One&display=swap" rel="stylesheet">
        <link href="https://fonts.googleapis.com/css2?family=Dela+Gothic+One&family=Kosugi+Maru&display=swap" rel="stylesheet">
        <link rel="stylesheet" href="css/reset.css">
        <link rel="stylesheet" href="css/ui.css">
        <link rel="stylesheet" href="css/top.css">
        <title>game client</title>
    </head>
    <body>
        <?php

            if($flg){

                echo '<noscript>You need to enable JavaScript to play this game.</noscript>';
                echo '<div id="frame"></div>';
                echo '<script type="text/javascript">let arg = "' . htmlspecialchars(preg_split("/;/", $player_name)[0]) . '";</script>';
                echo '<script type="text/javascript" src="js/ui.js"></script>';
                echo '<script type="text/javascript" src="js/ws.js"></script>';

            }else{

                if(strlen($player_name) > 50){
                    echo "<div>エラー：50文字を超えるプレイヤー名は設定できません。</div>";
                }

                echo '<div class="bg_pattern Diagonal"></div>';
                echo '<div id="bg_left"></div>';
                echo '<div id="bg_right"></div>';
                echo '<h1 id="title">ＥＴＥＲＮＡＬ ＡＮＴＩ</h1>';
                echo '<h2 id="gametitle">Paper Coppers</h1>';
                echo '<h3 id="sub_title"></h2>';
                echo '<form action="game_client.php" method="POST">';
                echo '<table>';
                echo '<tr id="input_name">';
                echo '<td><input type="text" id="name" name="player_name" value="" placeholder="プレイヤー名を入力"></td>';
                echo '</tr>';
                echo '<tr>';
                echo '<td><input type="image" id="matching" name="submit" alt="マッチング開始" src="img/matching.png"></td>';
                echo '</tr>';
                echo '</table>';
                echo '</form>';
                echo '<video preload="auto" src="video/test_video.mp4" loop autoplay muted playsinline></video>';

            }

        ?>
    </body>
</html>