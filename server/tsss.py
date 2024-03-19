import socket
import threading #マルチスレッディング用。Wait() -> join()
import time
import random

class TSSS:
    
    __SUB_STREAM_ID_SIZE = int(32 / 8)
    __PAYLOAD_LENGTH_SIZE = int(32 / 8)
    __TAG_SIZE = __SUB_STREAM_ID_SIZE + __PAYLOAD_LENGTH_SIZE

    __BUFFER_SIZE = 65535
    __BYTE_ORDER = "big"

    is_available = property(fget=(lambda self: self.__is_available))

    def GetWriteIFActivation(self, alloc_id:int) -> bool:
        return self.__alloc_list[alloc_id]["send_act"]

    def __init__(self, sock:socket.socket, max_client_num:int) -> None:
        
        self.__recv = None
        self.__send = None
        self.__alloc_lock = threading.Lock()
        self.__is_available = False #public get
        
        self.__sock = sock
        self.__max_client_num = max_client_num
        self.__alloc_list = [{
            
            "ssid": None,

            "send_lock": threading.Lock(),
            "send_buf": bytes(),
            "send_act": False,

            "recv_lock": threading.Lock(),
            "recv_buf": bytearray(),
            "recv_act": False,
        
        } for alloc_id in range(0, self.__max_client_num)]


    #起動
    def Start(self) -> None:

        self.__recv = threading.Thread(target=self.__StartReceiver)
        self.__recv.setDaemon(True)
        self.__recv.start()

        #self.__send = threading.Thread(target=self.__StartSender)
        #self.__send.setDaemon(True)
        #self.__send.start()

        self.__is_available = True


    
    def __StartSender(self) -> None:
        
        alloc_id = 0
        while True:
            
            if self.__alloc_list[alloc_id]["ssid"] is None:
                alloc_id = (alloc_id + 1) % self.__max_client_num
                continue

            time.sleep(random.randint(0, 100) * 0.001)
            self.__alloc_list[alloc_id]["send_lock"].acquire(blocking=True)
            
            if len(self.__alloc_list[alloc_id]["send_buf"]) != 0:
                self.__sock.sendall(self.__alloc_list[alloc_id]["send_buf"])
                self.__alloc_list[alloc_id]["send_buf"] = bytes()

            if self.__alloc_list[alloc_id]["send_lock"].locked():
                self.__alloc_list[alloc_id]["send_lock"].release()

            alloc_id = (alloc_id + 1) % self.__max_client_num
            time.sleep(0.01)


    def __StartReceiver(self) -> None:

        while True:

            tag = bytearray(TSSS.__TAG_SIZE)
            self.__sock.recv_into(tag, TSSS.__TAG_SIZE)
            
            sub_stream_id = int.from_bytes(bytes(tag[0:TSSS.__SUB_STREAM_ID_SIZE]), self.__BYTE_ORDER)
            payload_length = int.from_bytes(bytes(tag[TSSS.__SUB_STREAM_ID_SIZE:TSSS.__TAG_SIZE]), self.__BYTE_ORDER)

            #print("SSID:" + str(sub_stream_id))

            alloc_id = self.GetAllocID(sub_stream_id)
            if alloc_id == -1:
                #print("NOT HAVE ALLOC ID")
                alloc_id = self.Allocate(sub_stream_id)
                #print(alloc_id)
                if alloc_id == -1:
                    #print("NOT ALLOCATE BY AUTHORITY")
                    payload = bytearray(self.__BUFFER_SIZE)
                    self.__sock.recv_into(payload, payload_length)
                    continue

            if payload_length == 0:
                print("    SUB STREAM [ssid=" + str(sub_stream_id) + "] -> SWITCHING [" + ("WRITE-INACTIVE" if self.__alloc_list[alloc_id]["send_act"] else "WRITE-ACTIVE") + "]")
                self.__alloc_list[alloc_id]["send_act"] = not self.__alloc_list[alloc_id]["send_act"]
                continue

            payload = bytearray(self.__BUFFER_SIZE)
            read_size = self.__sock.recv_into(payload, payload_length)

            if self.__alloc_list[alloc_id]["recv_act"]:
                time.sleep(random.randint(1, 100) * 0.001)
                self.__alloc_list[alloc_id]["recv_lock"].acquire(blocking=True)
                self.__alloc_list[alloc_id]["recv_buf"] = self.__alloc_list[alloc_id]["recv_buf"] + payload[0:read_size]
                self.__alloc_list[alloc_id]["recv_lock"].release()

            time.sleep(0.001)


    def GetAllocID(self, ssid:int) -> int:
        for alloc_id in range(0, self.__max_client_num):
            if self.__alloc_list[alloc_id]["ssid"] == ssid:
                return alloc_id
        return -1


    def Allocate(self, ssid:int) -> bool:
        for alloc_id in range(0, self.__max_client_num):
            if self.__alloc_list[alloc_id]["ssid"] is None:
                self.__alloc_list[alloc_id]["ssid"] = ssid
                return alloc_id
        return -1
        


    def ActivateReadIF(self, alloc_id:int) -> None:
        
        if self.__alloc_list[alloc_id]["ssid"] is None:
            return

        if self.__alloc_list[alloc_id]["recv_act"]:
            return

        self.__alloc_list[alloc_id]["recv_act"] = True
        
        tag = self.__alloc_list[alloc_id]["ssid"].to_bytes(TSSS.__SUB_STREAM_ID_SIZE, self.__BYTE_ORDER) + (0).to_bytes(TSSS.__PAYLOAD_LENGTH_SIZE, self.__BYTE_ORDER)
        time.sleep(random.randint(1, 100) * 0.001)
        self.__alloc_list[alloc_id]["send_lock"].acquire(blocking=True)
        self.__sock.sendall(tag)
        #self.__alloc_list[alloc_id]["send_buf"] = self.__alloc_list[alloc_id]["send_buf"] + tag
        self.__alloc_list[alloc_id]["send_lock"].release()
        print("    SUB STREAM [ssid=" + str(self.__alloc_list[alloc_id]["ssid"]) + "] -> SWITCHING [READ-ACTIVE]")
        #print(self.__alloc_list[alloc_id]["send_buf"])



    def DeactivateReadIF(self, alloc_id:int) -> None:

        if self.__alloc_list[alloc_id]["ssid"] is None:
            return

        if not self.__alloc_list[alloc_id]["recv_act"]:
            return

        self.__alloc_list[alloc_id]["recv_act"] = False
        
        tag = self.__alloc_list[alloc_id]["ssid"].to_bytes(TSSS.__SUB_STREAM_ID_SIZE, self.__BYTE_ORDER) + (0).to_bytes(TSSS.__PAYLOAD_LENGTH_SIZE, self.__BYTE_ORDER)
        time.sleep(random.randint(1, 100) * 0.001)
        self.__alloc_list[alloc_id]["send_lock"].acquire(blocking=True)
        self.__sock.sendall(tag)
        #self.__alloc_list[alloc_id]["send_buf"] = self.__alloc_list[alloc_id]["send_buf"] + tag
        self.__alloc_list[alloc_id]["send_lock"].release()
        print("    SUB STREAM [ssid=" + str(self.__alloc_list[alloc_id]["ssid"]) + "] -> SWITCHING [READ-INACTIVE]")
        #print(self.__alloc_list[alloc_id]["send_buf"])


    def Send(self, alloc_id:int, buffer:bytearray) -> None:

        if self.__alloc_list[alloc_id]["ssid"] is None:
            return

        if not self.__alloc_list[alloc_id]["send_act"]:
            return

        tag = self.__alloc_list[alloc_id]["ssid"].to_bytes(4, self.__BYTE_ORDER) + len(buffer).to_bytes(4, self.__BYTE_ORDER)
        time.sleep(random.randint(1, 100) * 0.001)
        self.__alloc_list[alloc_id]["send_lock"].acquire(blocking=True)
        self.__sock.sendall(self.__alloc_list[alloc_id]["send_buf"] + tag + bytes(buffer))
        #self.__alloc_list[alloc_id]["send_buf"] = self.__alloc_list[alloc_id]["send_buf"] + tag + bytes(buffer)
        self.__alloc_list[alloc_id]["send_lock"].release()



    def Receive(self, alloc_id:int, buffer:bytearray) -> int:

        read_size = 0
        while read_size == 0:

            if self.__alloc_list[alloc_id]["ssid"] is None:
                return 0

            time.sleep(random.randint(1, 100) * 0.001)
            self.__alloc_list[alloc_id]["recv_lock"].acquire(blocking=True)

            if self.__alloc_list[alloc_id]["recv_buf"] is None:
                self.__alloc_list[alloc_id]["recv_lock"].release()
                return 0

            if len(self.__alloc_list[alloc_id]["recv_buf"]) != 0:
                
                read_size = min(len(buffer), len(self.__alloc_list[alloc_id]["recv_buf"]))
                for i in range(0, read_size):
                    buffer[i] = self.__alloc_list[alloc_id]["recv_buf"][i]
                self.__alloc_list[alloc_id]["recv_buf"] = bytearray()

            elif not self.__alloc_list[alloc_id]["recv_act"]: #Deactivation以前に受け取ったデータの終端 -> Inactiveで、バッファにデータがない
                self.__alloc_list[alloc_id]["recv_lock"].release()
                return 0

            self.__alloc_list[alloc_id]["recv_lock"].release()

                
        return read_size



    def Release(self, alloc_id:int) -> None:

        time.sleep(random.randint(1, 100) * 0.001)
        self.__alloc_list[alloc_id] = {
            
            "ssid": None,

            "send_lock": threading.Lock(),
            "send_buf": bytes(),
            "send_act": False,

            "recv_lock": threading.Lock(),
            "recv_buf": bytearray(),
            "recv_act": False,
        
        }




class TcpSubStream:

    #method
    def __init__(self, tsss:TSSS, sub_stream_id:int) -> None:
        self.__tsss = tsss
        self.__sub_stream_id = sub_stream_id
        self.__alloc_id = self.__tsss.GetAllocID(sub_stream_id)
        if self.__alloc_id == -1:
            self.__alloc_id = self.__tsss.Allocate(sub_stream_id)

    def ActivateReadIF(self) -> None:
        self.__tsss.ActivateReadIF(self.__alloc_id)

    def DeactivateReadIF(self) -> None:
        self.__tsss.DeactivateReadIF(self.__alloc_id)

    def Send(self, buffer:bytearray) -> None:
        self.__tsss.Send(self.__alloc_id, buffer)

    def Receive(self, buffer:bytearray) -> int:
        return self.__tsss.Receive(self.__alloc_id, buffer)

    def Close(self):
        self.__tsss.Release(self.__alloc_id)

    def WaitWriteIFActivation(self) -> None:
        while not self.__tsss.GetWriteIFActivation(self.__alloc_id):
            time.sleep(0.1)

    def WaitWriteIFDeactivation(self) -> None:
        while self.__tsss.GetWriteIFActivation(self.__alloc_id):
            time.sleep(0.1)
    
    def GetWriteIFActivation(self) -> bool:
        return self.__tsss.GetWriteIFActivation(self.__alloc_id)

    sub_stream_id = property(fget=lambda self: self.__sub_stream_id)
    is_write_if_active = property(fget=lambda self: self.GetWriteIFActivation())