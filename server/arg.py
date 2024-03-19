import ipaddress

def check_arg_ip_addr(arg:str):
    flg = False
    try:
        ipaddress.ip_address(arg)
        flg = True
    except:
        flg = False
    return flg

def check_arg_port(arg:str):
    flg = False
    try:
        tmp = int(arg)
        if 0 < tmp and tmp <= 65535: 
            flg = True
    except:
        flg = False
    return flg

def check_arg_max_game_server_unit_num(arg:str):
    flg = False
    try:
        tmp = int(arg)
        if 0 < tmp: 
            flg = True
    except:
        flg = False
    return flg