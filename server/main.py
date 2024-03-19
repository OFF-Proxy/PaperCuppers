import sys
import socket
import arg
import gobb_game_server

remote_ip_addr = ""
remote_port = 0
max_game_server_unit_num = 0

arg_len = len(sys.argv)
flg = False
if arg_len == 1:
    remote_ip_addr = "127.0.0.1"
    remote_port = 65535
    max_game_server_unit_num = 1
    flg = True
elif arg_len == 2:
    if arg.check_arg_ip_addr(sys.argv[1]):
        remote_ip_addr = sys.argv[1]
        remote_port = 65535
        max_game_server_unit_num = 1
        flg = True
elif arg_len == 3:
    if arg.check_arg_ip_addr(sys.argv[1]) and arg.check_arg_port(sys.argv[2]):
        remote_ip_addr = sys.argv[1]
        remote_port = int(sys.argv[2])
        max_game_server_unit_num = 1
        flg = True
elif arg_len == 4:
    if arg.check_arg_ip_addr(sys.argv[1]) and arg.check_arg_port(sys.argv[2]) and arg.check_arg_max_game_server_unit_num(sys.argv[3]):
        remote_ip_addr = sys.argv[1]
        remote_port = int(sys.argv[2])
        max_game_server_unit_num = int(sys.argv[3])
        flg = True
else:
    print("Command line arguments are inappropriate.")
    print("Please enter in the following format.")
    print("[(optional) remote ip addr] [(optional) remote port] [(optional) max_game_server_unit_num]")

if flg:
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM, 6)
        sock.connect((remote_ip_addr, remote_port))
    except:
        flg = False

if flg:
    print("Start-Up Game Server.")
    game_server = gobb_game_server.GobbGameServer(sock, max_game_server_unit_num)
    game_server.Run()





#サーバ側でもSSのクローズ検知
#-> ControlClientのタスクを終了する

#マッチング中に抜けたとき、エラーを残ったクライアントに伝達するのと、サーバユニットを開放する
#