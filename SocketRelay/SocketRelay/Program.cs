using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;

namespace SocketRelaySocketRelayAgent
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                SocketRelayAgent socket_relay_agent = new SocketRelayAgent("127.0.0.1", 65535, "127.0.0.1", 8000, 2);
                socket_relay_agent.Start();
                socket_relay_agent.Engine.Wait();
            }

            if (CheckArgs(args))
            {
                SocketRelayAgent socket_relay_agent = new SocketRelayAgent(args[0], Convert.ToInt32(args[1]), args[2], Convert.ToInt32(args[3]), Convert.ToInt32(args[4]));
                socket_relay_agent.Start();
                socket_relay_agent.Engine.Wait();
            }
        }

        private static bool CheckArgs(string[] args)
        {
            if (args.Length != 5)
            {
                Console.WriteLine("If you set the arguments manually, you need 5 arguments.");
                Console.WriteLine("[server-side ipv4 address] [server-side port] [client-side ipv4 address] [client-side port] [max client num]");
                return false;
            }

            bool flg = true;

            IPAddress ip_addr;

            if (!IPAddress.TryParse(args[0], out ip_addr))
            {
                flg = false;
                Console.WriteLine("Argument 1 \"server-side ipv4 address\" is not correct format.");
                Console.WriteLine("Enter in the following format. [n.n.n.n]");
                Console.WriteLine();
            }

            if (!PortNumber.TryParse(args[1]))
            {
                flg = false;
                Console.WriteLine("Argument 2 \"server-side port\" is not correct value.");
                Console.WriteLine("Enter in the following range. [0-65535]");
                Console.WriteLine();
            }

            if (!IPAddress.TryParse(args[2], out ip_addr))
            {
                flg = false;
                Console.WriteLine("Argument 3 \"client-side ipv4 address\" is not correct format.");
                Console.WriteLine("Enter in the following format. [n.n.n.n]");
                Console.WriteLine();
            }

            if (!PortNumber.TryParse(args[3]))
            {
                flg = false;
                Console.WriteLine("Argument 4 \"client-side port\" is not correct value.");
                Console.WriteLine("Enter in the following range. [0-65535]");
                Console.WriteLine();
            }

            try
            {
                if (!(0 <= Convert.ToInt32(args[4]) && Convert.ToInt32(args[4]) < int.MaxValue) && Convert.ToInt32(args[4]) % 2 == 0)
                {
                    flg = false;
                    Console.WriteLine("Argument 5 \"max client num\" is not correct value.");
                    Console.WriteLine("Enter one or more numbers.");
                    Console.WriteLine();
                }
            }
            catch
            {
                flg = false;
                Console.WriteLine("Argument 5 \"max client num\" is not correct value.");
                Console.WriteLine("Enter one or more numbers.");
                Console.WriteLine();
            }

            if (!flg)
            {
                Console.WriteLine("Enter the arguments in the following order.");
                Console.WriteLine("[server-side ipv4 address] [server-side port] [client-side ipv4 address] [client-side port] [max client num]");
            }

            return flg;

        }

    }

    internal static class PortNumber
    {
        internal static bool TryParse(string port)
        {
            try
            {
                return 0 <= Convert.ToInt32(port) && Convert.ToInt32(port) <= 65535;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class Dev
    {
        internal static void DisplayByteArray(byte[] byteArray, int length)
        {
            for (int b = 0; b < length; b++)
            {
                string t = Convert.ToString(byteArray[b], 16);
                Console.Write(t.Length == 2 ? t : "0" + t);
            }
            Console.WriteLine();
        }
    }

    internal class SocketRelayAgent
    {
        private string server_side_local_ip_address = null;
        private int server_side_local_port = 0;
        private string client_side_local_ip_address = null;
        private int client_side_local_port = 0;
        private int max_client_num = 0;

        private bool is_available = false;
        private Task engine = null;

        internal Task Engine
        {
            get { return engine; }
        }

        internal bool IsAvailable
        {
            get { return is_available; }
        }

        internal SocketRelayAgent(string server_side_ip_addr, int server_side_port, string client_side_ip_addr, int client_side_port, int max_client_num)
        {
            server_side_local_ip_address = server_side_ip_addr;
            server_side_local_port = server_side_port;
            client_side_local_ip_address = client_side_ip_addr;
            client_side_local_port = client_side_port;
            this.max_client_num = max_client_num;
        }

        internal void Start()
        {
            engine = Task.Factory.StartNew(() => StartEngine());
        }

        private void StartEngine()
        {
            Console.WriteLine("> Start-Up.");
            Console.WriteLine("> Waiting for TCP connection from server.");
            Console.WriteLine("> ...");

            //Establish TCP Connection
            IPAddress local_ip_address = IPAddress.Parse(server_side_local_ip_address);
            int local_port = server_side_local_port;
            TcpListener tcp_listener = new TcpListener(local_ip_address, local_port);
            tcp_listener.Start();
            TcpClient tcp_client = tcp_listener.AcceptTcpClient();

            Console.WriteLine("> Done.");
            Console.WriteLine("> Start TSSS.");

            //Start TCP Sub Stream Splitting
            NetworkStream tcp_stream = tcp_client.GetStream();
            TSSS tsss = new TSSS(tcp_stream, max_client_num);
            tsss.Start();
            CancellationTokenSource tcp_unavailable_cancellation = new CancellationTokenSource();

            Console.WriteLine("> Done.");
            Console.WriteLine("> Starting Socket Relay allocation");

            HttpListener http_listener = new HttpListener();
            http_listener.Prefixes.Add("http://" + client_side_local_ip_address + ":" + client_side_local_port + "/");
            http_listener.Start();

            int buffer_size = 65535; //
            TimeSpan keep_alive = new TimeSpan(0, 20, 0); //

            HttpListenerWebSocketContext[] websocket_context_list = new HttpListenerWebSocketContext[max_client_num];
            SocketRelay[] socket_relay_list = new SocketRelay[max_client_num];
            bool[] allocation_flag_list = new bool[max_client_num];
            Array.Fill(allocation_flag_list, false, 0, max_client_num);

            is_available = true;

            //Start Socket Relay
            //int c = 0;
            while (tsss.IsAvailable)
            {
                Console.WriteLine("[ SOCKET RELAY ] WAITING HTTP REQUEST");
                HttpListenerContext http_context = http_listener.GetContext();
                Console.WriteLine("[ SOCKET RELAY ] ACCEPT HTTP REQUEST");

                //リクエストがWebSocketでない場合は400エラーを返す
                if (!http_context.Request.IsWebSocketRequest)
                {
                    http_context.Response.StatusCode = 400;
                    http_context.Response.Close();
                    continue;
                }

                bool flg = false;
                for (int c = 0; c < max_client_num; c++)
                {
                    if (!allocation_flag_list[c] || !socket_relay_list[c].IsAvailable)
                    {
                        websocket_context_list[c] = http_context.AcceptWebSocketAsync(null, buffer_size, keep_alive).Result;
                        Console.WriteLine("[ SOCKET RELAY ] ACCEPT WEBSOCKET");
                        //socket_relay_list[c] = null;
                        socket_relay_list[c] = new SocketRelay(websocket_context_list[c].WebSocket, new TcpSubStream(tsss, c));
                        socket_relay_list[c].Start(tcp_unavailable_cancellation.Token);
                        allocation_flag_list[c] = socket_relay_list[c].IsAvailable;
                        Console.WriteLine($"[ SOCKET RELAY ] ALLOCATE -> ID = {c}");
                        flg = true;
                        break;
                    }
                }

                if (!flg)
                {
                    http_context.Response.StatusCode = 503;
                    http_context.Response.Close();
                }


                //・max_client_numを超えたら即503 Service Unavailable
                //・WebSocketのAcceptだけして、待機（待機許容数を超えたら503）

                /*
                while (true)
                {
                    Console.WriteLine("IN");
                    if (!allocation_flag_list[c] || !socket_relay_list[c].IsAvailable)
                    {
                        Console.WriteLine("BREAK");
                        break;
                    }
                    c = (c + 1) % max_client_num;
                    Task.Delay(20).Wait();
                }

                websocket_context_list[c] = http_context.AcceptWebSocketAsync(null, buffer_size, keep_alive).Result;
                Console.WriteLine("ACCEPT WEBSOCK");
                //socket_relay_list[c] = null;
                socket_relay_list[c] = new SocketRelay(websocket_context_list[c].WebSocket, new TcpSubStream(tsss, c));
                socket_relay_list[c].Start(tcp_unavailable_cancellation.Token);
                allocation_flag_list[c] = socket_relay_list[c].IsAvailable;
                Console.WriteLine($"ALLOC SOCKET RELAY -> ID:{c}");
                */


            }

            tcp_unavailable_cancellation.Cancel();
            is_available = false;

        }

    }

    internal class SocketRelay
    {
        private WebSocket websocket;
        private TcpSubStream tcp_sub_stream;

        private Task[] socket_relay = new Task[3];
        private bool is_available = false;

        internal bool IsAvailable
        {
            get { return is_available; }
        }

        internal SocketRelay(WebSocket websocket, TcpSubStream tcp_sub_stream)
        {
            this.websocket = websocket;
            this.tcp_sub_stream = tcp_sub_stream;
        }

        internal void Start(CancellationToken cancellation)
        {
            int status = tcp_sub_stream.ActivateReadInterface();
            //Console.WriteLine($"READ ACTIVE -> SUB STREAM ID = {tcp_sub_stream.SubStreamID}");
            //tcp_sub_stream.Display();
            if (status == -1)
            {
                return;
            }
            CancellationTokenSource relay_cancellation = new CancellationTokenSource();
            tcp_sub_stream.WaitWriteInterfaceActivation();
            //Console.WriteLine($"WRITE ACTIVE -> SUB STREAM ID = {tcp_sub_stream.SubStreamID}");
            socket_relay[0] = Task.Factory.StartNew(() => ControlCancellation(cancellation, relay_cancellation));
            socket_relay[1] = Task.Factory.StartNew(() => ForwardToClient(relay_cancellation.Token));
            socket_relay[2] = Task.Factory.StartNew(() => ForwardToServer(relay_cancellation.Token));
            is_available = true;
        }

        //片方が閉じたらもう片方も閉じるクローズの伝播
        //socketrelay側でクローズ->Switchingでゲーム側でもクローズ


        private void ForwardToServer(CancellationToken relay_cancellation)
        {
            byte[] buffer = new byte[65535];
            ArraySegment<byte> ws_receive_buffer = new ArraySegment<byte>(buffer, 0, buffer.Length);
            while (!relay_cancellation.IsCancellationRequested)
            {
                WebSocketReceiveResult ws_receive = websocket.ReceiveAsync(ws_receive_buffer, relay_cancellation).Result;
                if (ws_receive.Count != 0)
                {
                    int status = tcp_sub_stream.Write(ws_receive_buffer.ToArray(), 0, ws_receive.Count);
                    if (status == -1)
                    {
                        continue;
                    }
                }
                Task.Delay(1).Wait();
                //Dev.DisplayByteArray(ws_receive_buffer.ToArray(), ws_receive.Count);
            }
        }

        private void ForwardToClient(CancellationToken relay_cancellation)
        {
            byte[] tcp_receive_buffer = new byte[65535];
            while (!relay_cancellation.IsCancellationRequested)
            {
                int read_size = tcp_sub_stream.Read(tcp_receive_buffer, 0, tcp_receive_buffer.Length, relay_cancellation);
                if (read_size == -1)
                {
                    continue;
                }
                websocket.SendAsync(new ArraySegment<byte>(tcp_receive_buffer, 0, read_size), WebSocketMessageType.Binary, true, relay_cancellation).Wait();
                Task.Delay(1).Wait();
                //Dev.DisplayByteArray(tcp_receive_buffer, read_size);
            }
        }

        private void ControlCancellation(CancellationToken cancellation, CancellationTokenSource relay_cancellation)
        {
            while (true)
            {
                if (cancellation.IsCancellationRequested || websocket.State != WebSocketState.Open || !tcp_sub_stream.GetWriteInterfaceSwitch())
                {
                    Console.WriteLine($"[ SOCKET RELAY ] CLOSE -> ID = {tcp_sub_stream.SubStreamID}, WS STATE = {websocket.State}, W-IF = {(tcp_sub_stream.GetWriteInterfaceSwitch() ? "ACTIVE" : "INACTIVE")}, TCP CANCEL -> {cancellation.IsCancellationRequested}");
                    relay_cancellation.Cancel();
                    Close();
                    return;
                }
                Task.Delay(20).Wait();
            }
        }

        internal void Close()
        {
            if (is_available)
            {
                tcp_sub_stream.DeactivateReadInterface();
                tcp_sub_stream.WaitWriteInterfaceDeactivation();
                tcp_sub_stream.WaitWriteBufferBecomingEmpty();
                Task.Delay(1000).Wait();
                tcp_sub_stream.Close();
                tcp_sub_stream = null;
                if (websocket.State != WebSocketState.Closed && websocket.State != WebSocketState.CloseReceived)
                {
                    websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye.", CancellationToken.None).Wait();
                }
                socket_relay[0] = null;
                socket_relay[1] = null;
                socket_relay[2] = null;
                is_available = false;
            }
        }

    }


    /*

                                                                    Simple stream multiplexing over TCP



    < Abstract >
    TSSS（TCP Sub Stream Splitting : TCPサブストリーム分割）

    TSSSは、TSSSタグと呼ばれるタグによってデータを識別することで、一つのTCPコネクション上（あるいは、その上のTLSセッション上）で
    複数種類のアプリケーションデータを送受信することができます。タグVLANのようなイメージです。

    TSSSはあくまで"ストリーム"を仮想的に多重化することを目的としており、"コネクション"を多重化するわけではありません。
    したがって、多重化されたストリームごとのフロー制御や再送制御、輻輳制御などは行われず、これらの制御はTCPレベルのものに留まります。
    そのためパケットロスの大きい環境では、HOLブロッキング（Head of Line Blocking）が顕在化する可能性があります。
    また、多重化されたストリームごとのきめ細かい伝送制御、QoS制御などを実現する場合には、TSSSは不向きであるといえます。




    < format >

    TSSSのタグフォーマットを以下に示します。

    0                                     31
    +-------------------------------------+
    |           Sub Stream ID             |
    +-------------------------------------+
    |           Payload Length            |
    +-------------------------------------+
    :                                     :
    :         (Application Data)          :
    :                                     :

    ・Sub Stream ID  - ペイロードを届けるべきプロセスやアプリケーションを識別するための値です。
    ・Payload Length - ペイロード（すなわちアプリケーションデータ）の長さです。

    これら"Sub Stream ID"と"Payload Length"を併せてTSSSタグ（TSSS Tag）と呼びます。


    "Sub Stream ID"はTCPコネクション上のストリームを、サブストリーム（Sub Stream）と呼ばれる仮想的なデータ流路に分割します。
    サブストリームの両端の通信者は、"Sub Stream ID"の値によってアプリケーションデータを届けるべきプロセスやアプリケーションを識別します。
    したがって、送信側と受信側で各プロセスやアプリケーションに用いる"Sub Stream ID"を合意しておく必要があります。
    これは、各プロセスやアプリケーションであらかじめ決定しておいても、制御用サブストリームを構築して通知しても、それ以外の方法で通知しても構いません。
    "Sub Stream ID"が0であるサブストリームは将来の拡張用に予約されています。
    TSSSを実装するエンドポイントは、"Sub Stream ID"が0のサブストリームとは別のサブストリームを一つ以上利用できるようにする必要があります。




    < stream unit >

    各サブストリームは、論理的には、ストリームユニット（Stream Unit）と呼ばれる最小構成単位二つから成ります。
    一つのサブストリームは、送信方向（Sender）と受信方向（Receiver）のストリームユニットを一つずつ内包し、これらを用いることで、最大で全二重での双方向通信が可能となります。


    また、ストリームユニットはそれぞれ両端にインターフェース（Interface）と呼ばれる書き込み口と読み取り口を持ち、
    書き込み口はライトインターフェース（Write Interface）、読み取り口はリードインターフェース（Read Interface）と呼ばれます。

    サブストリームの構成のイメージを以下に示します。


                            sub stream
                            +----------------------------------------------------------------+
                            |                                                                |
                            |    Alice's sender stream unit (=Bob's receiver stream unit)    |
            write interface = -------------------------------------------------------------> = read interface
    Alice                   |                                                                |                 Bob
            read interface  = <------------------------------------------------------------- = write interface       
                            |    Bob's sender stream unit (=Alice's receiver stream unit)    |
                            |                                                                |
                            +----------------------------------------------------------------+






    < state of interfaces >

    ストリームユニットの各インターフェースは、非アクティブ（Inactive）、アクティブ（Active）のいずれかの状態を取ります。

    非アクティブ状態は、インターフェースが起動待ちにある状態です。
    非アクティブ状態にあるリードインターフェースからは後述するスイッチングタグの読み込みのみ、非アクティブ状態にあるライトインターフェースからは
    スイッチングタグの書き込みのみが許可されます。
    スイッチングタグ以外のTSSSタグ及びペイロードデータを書き込んだ/受け取った場合は、バッファリングされず即座にドロップ（Drop）されます。


    アクティブ状態は、インターフェースが起動している状態です。
    TSSSタグおよびペイロードデータの両方について、リードインターフェースからの読み込み、およびライトインターフェースへの書き込みが可能です。

    非アクティブ状態からアクティブ状態にすることをアクティブ化（Activate）、アクティブ状態から非アクティブ状態にすることを非アクティブ化（Deactivate）といいます。


    リードインターフェースは、実装先プロセスやアプリケーションからの指示を受けてアクティブ化、非アクティブ化されます。

    対して、ライトインターフェースは、相手側のリードインターフェースのアクティブ化、非アクティブ化に伴ってアクティブ化、非アクティブ化されます。
    リードインターフェースが非アクティブ状態からアクティブ状態へと遷移すると、後述するスイッチングタグが相手に送信されます。
    スイッチングタグを受け取った相手は、自身のライトインターフェースをアクティブ化します。

    同様に、リードインターフェースがアクティブ状態から非アクティブ状態へ遷移すると、スイッチングタグが相手に送信され、
    スイッチングタグを受け取った相手は自身のライトインターフェースを非アクティブ化します。

    初期状態ではいずれのインターフェースも非アクティブです。





    < switching tag >

    スイッチングタグ（Switching Tag）は、自身のリードインターフェースがアクティブ状態へと遷移したことを相手に通知するための特殊なTSSSタグです。
    "Sub Stream ID"でサブストリームを指定し、"Payload Length"に0を設定します。したがって、ペイロードデータを持ちません。
    スイッチングタグは、インターフェースの非アクティブ、アクティブにかかわらず受け取り、書き込みが許可されています。





    < state of steram unit >

    ストリームユニットは、開通（Opened）、閉塞（Closed）のいずれかの状態を取ります。

    開通状態は、一つのストリームユニットの両端にあるインターフェースの双方がアクティブである状態です。
    閉塞状態は、一つのストリームユニットの両端にあるインターフェースのいずれかが非アクティブである状態です。

    ストリームユニットの開通、閉塞状態はストリームユニットごとに独立しています。つまり、双方向通信を行う場合は双方のストリームユニットについて開通状態にする必要があり、
    またあえて片方向のストリームユニットのみを開通状態にすることでサブストリーム単位の論理的な単方向通信を実現することもできます。






    < appendix >

    ※付録1：ストリームユニットとインターフェースの状態


    ex)
                          Alice                                   Bob 
    Read-Inactive           |                                      | Read-Inactive
    Write-Inactive          |                                      | Write-Inactive
                            |                                      |                            <---- (1)
                            |                                      |
                            |            Switching Tag             |
    Change: Read-Active     |------------------------------------->| Switching: Write-Active
                            |                                      |
                            |                                      |
                            |                                      |                            <---- (2)
                            |                                      |
                            |            Switching Tag             |    
    Change: Write-Active    |<-------------------------------------| Change: Read-Active
                            |                                      |
                            |                                      |                            <---- (3)
                            |                                      |
                            |            Switching Tag             |    
    Change: Read-Inactive   |------------------------------------->| Switching: Write-Inactive
                            |                                      |
                            |                                      |
                            |                                      |
                            |                                      |                            <---- (4)
                            |                                      |
                            |            Switching Tag             |
    Change: Write-Inactive  |<-------------------------------------| Change: Read-Inactive 
                            |                                      |
                            |                                      |                            <---- (5)
                            |                                      |



    (1) All stream units are closing.

    +-------+----------------+-----------------+
    |   *   | Read Interface | Write Interface |
    +-------+----------------+-----------------+
    | Alice |    Inactive    |     Inactive    |
    +-------+----------------+-----------------+
    |  Bob  |    Inactive    |     Inactive    |
    +-------+----------------+-----------------+

    +----------------------------+-------------+
    | Alice's sender stream unit |    Closed   |
    +----------------------------+-------------+
    |  Bob's sender steram unit  |    Closed   |
    +----------------------------+-------------+



    (2) One-way communication from Bob.

    +-------+----------------+-----------------+
    |   *   | Read Interface | Write Interface |
    +-------+----------------+-----------------+
    | Alice |     Active     |     Inactive    |
    +-------+----------------+-----------------+
    |  Bob  |    Inactive    |      Active     |
    +-------+----------------+-----------------+

    +----------------------------+-------------+
    | Alice's sender stream unit |    Closed   |
    +----------------------------+-------------+
    |  Bob's sender steram unit  |    Opened   |
    +----------------------------+-------------+



    (3) Two-way communication.

    +-------+----------------+-----------------+
    |   *   | Read Interface | Write Interface |
    +-------+----------------+-----------------+
    | Alice |     Active     |      Active     |
    +-------+----------------+-----------------+
    |  Bob  |     Active     |      Active     |
    +-------+----------------+-----------------+

    +----------------------------+-------------+
    | Alice's sender stream unit |    Opened   |
    +----------------------------+-------------+
    |  Bob's sender steram unit  |    Opened   |
    +----------------------------+-------------+



    (4) One-way communication from Alice.

    +-------+----------------+-----------------+
    |   *   | Read Interface | Write Interface |
    +-------+----------------+-----------------+
    | Alice |    Inactive    |      Active     |
    +-------+----------------+-----------------+
    |  Bob  |     Active     |     Inactive    |
    +-------+----------------+-----------------+

    +----------------------------+-------------+
    | Alice's sender stream unit |    Opened   |
    +----------------------------+-------------+
    |  Bob's sender steram unit  |    Clsoed   |
    +----------------------------+-------------+



    (5) All stream units are closing.

    +-------+----------------+-----------------+
    |   *   | Read Interface | Write Interface |
    +-------+----------------+-----------------+
    | Alice |    Inactive    |     Inactive    |
    +-------+----------------+-----------------+
    |  Bob  |    Inactive    |     Inactive    |
    +-------+----------------+-----------------+

    +----------------------------+-------------+
    | Alice's sender stream unit |    Closed   |
    +----------------------------+-------------+
    |  Bob's sender steram unit  |    Closed   |
    +----------------------------+-------------+







    ※この機構は試験的なものであり、今後大幅に変更される場合があります。



    memo
    
    Read-IFのアクティブ化/非アクティブ化をスイッチングタグで通知して、相手側のWrite-IFをアクティブ化/非アクティブ化し、サブストリームのステートを共有する話やけれど、これって、通信路上での遅延を考慮してないわけやんか
    ストリームユニットの両端のインターフェースの状態を同期的に扱いたいんやったら、これを何とかしやなかん

    Read側は閉じたつもりでも、Write側ではスイッチングタグが届くまでスイッチ状態を取得してもアクティブのまま
    送受信に関しては、Read側でドロップするだけでいいけど、このスイッチ状態を上位のアプリケーションが使う場合に今回みたいな問題が出てくるわけや




    相手にどこまで受信したかを知らせるためには、シーケンス番号がどこの時点で切断したかとかないとだめなのでは？
    （ACKがあればいいのか。）
    |
    V
    ACKって便利で、受けっとったよっていうただの確認って思いがちやけど、相手にデータが何バイトまで伝送されてるか分かるから、RSTとかしたとしても、相手がデータをどこまで受信してくれてるかわかるっていう優れもの
    っていうことに気づいた。

    */


    internal class TSSS
    {
        private NetworkStream tcp_stream = null;
        private int max_sub_stream_num = 0;

        private Task read_authority = null;
        private Task write_authority = null;

        private int[] sub_stream_id_list = null;

        private bool[] read_interface_active_list = null;
        private MemoryStream[] receiver_stream_unit_list = null;
        private object[] receiver_stream_unit_lock_list = null;
        private long[] receiver_stream_unit_requester_index_list = null;
        private long[] receiver_stream_unit_authority_index_list = null;

        private bool[] write_interface_active_list = null;
        private MemoryStream[] sender_stream_unit_list = null;
        private object[] sender_stream_unit_lock_list = null;
        private int[] write_request_length_list = null;

        private object create_sub_stream_lock = new object();
        private bool is_tsss_available = false;
        private RNGCryptoServiceProvider rnd = new RNGCryptoServiceProvider();
        private CancellationTokenSource cancellation = null;

        internal bool IsAvailable
        {
            get { return is_tsss_available && !cancellation.IsCancellationRequested; }
        }

        internal const int TAG_SIZE = 64 / 8;
        internal const int SUB_STREAM_ID_SIZE = 32 / 8;
        internal const int PAYLOAD_LENGTH_SIZE = 32 / 8;

        internal TSSS(NetworkStream tcp_stream, int max_sub_stream_num)
        {
            this.tcp_stream = tcp_stream;
            this.max_sub_stream_num = max_sub_stream_num;

            sub_stream_id_list = new int[max_sub_stream_num];

            read_interface_active_list = new bool[max_sub_stream_num];
            receiver_stream_unit_list = new MemoryStream[max_sub_stream_num];
            receiver_stream_unit_lock_list = new object[max_sub_stream_num];
            receiver_stream_unit_requester_index_list = new long[max_sub_stream_num];
            receiver_stream_unit_authority_index_list = new long[max_sub_stream_num];

            write_interface_active_list = new bool[max_sub_stream_num];
            sender_stream_unit_list = new MemoryStream[max_sub_stream_num];
            sender_stream_unit_lock_list = new object[max_sub_stream_num];
            write_request_length_list = new int[max_sub_stream_num];

            for (int i = 0; i < max_sub_stream_num; i++)
            {
                sub_stream_id_list[i] = -1;

                read_interface_active_list[i] = false;
                receiver_stream_unit_list[i] = null;
                receiver_stream_unit_lock_list[i] = new object();
                receiver_stream_unit_requester_index_list[i] = 0;
                receiver_stream_unit_authority_index_list[i] = 0;

                write_interface_active_list[i] = false;
                sender_stream_unit_list[i] = null;
                sender_stream_unit_lock_list[i] = new object();
                write_request_length_list[i] = 0;
            }
        }

        ~TSSS()
        {
            Release();
        }

        internal void Start()
        {
            cancellation = new CancellationTokenSource();
            read_authority = Task.Factory.StartNew(() => StartReadAuthority());
            write_authority = Task.Factory.StartNew(() => StartWriteAuthority());
            is_tsss_available = true;
        }

        internal void Release()
        {
            is_tsss_available = false;
            cancellation.Cancel();

            read_authority.Wait(10);
            write_authority.Wait(10);
            read_authority = null;
            write_authority = null;

            for (int i = 0; i < max_sub_stream_num; i++)
            {
                CloseSubStream(i);
            }

            sub_stream_id_list = new int[max_sub_stream_num];

            read_interface_active_list = new bool[max_sub_stream_num];
            receiver_stream_unit_list = new MemoryStream[max_sub_stream_num];
            receiver_stream_unit_lock_list = new object[max_sub_stream_num];
            receiver_stream_unit_requester_index_list = new long[max_sub_stream_num];
            receiver_stream_unit_authority_index_list = new long[max_sub_stream_num];

            write_interface_active_list = new bool[max_sub_stream_num];
            sender_stream_unit_list = new MemoryStream[max_sub_stream_num];
            sender_stream_unit_lock_list = new object[max_sub_stream_num];
            write_request_length_list = new int[max_sub_stream_num];

            cancellation.Dispose();
            cancellation = null;

        }

        private void StartReadAuthority()
        {
            byte[] wait_time_buffer = new byte[1];
            byte[] tsss_tag = new byte[TAG_SIZE];
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    //Read TSSS-tag
                    int read_size = tcp_stream.Read(tsss_tag, 0, TAG_SIZE);
                    if (read_size != TAG_SIZE)
                    {
                        cancellation.Cancel();
                        return;
                    }

                    //Parse TSSS-tag
                    byte[] sub_stream_id_byte_word = new byte[SUB_STREAM_ID_SIZE];
                    byte[] payload_length_byte_word = new byte[PAYLOAD_LENGTH_SIZE];
                    Array.Copy(tsss_tag, 0, sub_stream_id_byte_word, 0, SUB_STREAM_ID_SIZE);
                    Array.Copy(tsss_tag, SUB_STREAM_ID_SIZE, payload_length_byte_word, 0, PAYLOAD_LENGTH_SIZE);
                    int sub_stream_id = Convert4ByteWordToInt32(sub_stream_id_byte_word);
                    int payload_length = Convert4ByteWordToInt32(payload_length_byte_word);

                    //Get Allocation-ID and create a Sub-Stream if it receive a new Sub-Stream-ID
                    int allocation_id = GetIndex(sub_stream_id);
                    if (allocation_id < 0)
                    {
                        allocation_id = CreateSubStream(sub_stream_id);
                        if (allocation_id == -1)
                        {
                            byte[] temp = new byte[payload_length];
                            tcp_stream.Read(temp, 0, payload_length);
                            continue;
                        }
                    }

                    //Switching
                    if (payload_length == 0)
                    {
                        Console.WriteLine($"[ TSSS READ    ] TAG INFO -> SUB STREAM = {sub_stream_id}, PAYLOAD LENGTH = {payload_length}{(payload_length == 0 ? " [SWITCHING]" : "")}");
                        write_interface_active_list[allocation_id] = !write_interface_active_list[allocation_id];
                        continue;
                    }

                    //Read payload
                    byte[] payload = new byte[payload_length];
                    read_size = tcp_stream.Read(payload, 0, payload_length);
                    if (read_size != payload_length)
                    {
                        cancellation.Cancel();
                        return;
                    }
                    string p = null;
                    for (int b = 0; b < payload_length; b++)
                    {
                        string t = Convert.ToString(payload[b], 16);
                        p = p + (t.Length == 2 ? t : "0" + t);
                    }
                    Console.WriteLine($"[ TSSS READ    ] TAG INFO -> SUB STREAM = {sub_stream_id}, PAYLOAD LENGTH = {payload_length} :: PAYLOAD -> {p}{(!read_interface_active_list[allocation_id] ? " [DROPED]" : "")}");

                    //Drop payload destined for inactive read interface
                    if (!read_interface_active_list[allocation_id])
                    {
                        continue;
                    }

                    //Payload buffering
                    Task.Factory.StartNew(() =>
                    {
                        WaitBeforeLocking(wait_time_buffer, 0x0f);
                        lock (receiver_stream_unit_lock_list[allocation_id])
                        {
                            try
                            {
                                receiver_stream_unit_list[allocation_id].Position = receiver_stream_unit_authority_index_list[allocation_id];
                                receiver_stream_unit_list[allocation_id].Write(payload, 0, payload_length);
                                receiver_stream_unit_authority_index_list[allocation_id] = receiver_stream_unit_authority_index_list[allocation_id] + payload_length;
                            }
                            catch
                            {
                                return;
                            }
                        }
                    });

                }
                catch
                {
                    cancellation.Cancel();
                    return;
                }
            }
        }

        private void StartWriteAuthority()
        {
            byte[] wait_time_buffer = new byte[1];
            int i = 0;
            while (!cancellation.IsCancellationRequested)
            {
                WaitBeforeLocking(wait_time_buffer, 0x0f);
                lock (sender_stream_unit_lock_list[i])
                {
                    try
                    {
                        if (write_request_length_list[i] != 0)
                        {
                            byte[] buffer = new byte[write_request_length_list[i]];
                            sender_stream_unit_list[i].Position = 0;
                            sender_stream_unit_list[i].Read(buffer, 0, buffer.Length);
                            tcp_stream.Write(buffer, 0, buffer.Length);
                            write_request_length_list[i] = 0;
                        }
                    }
                    catch
                    {
                        cancellation.Cancel();
                        return;
                    }
                }
                i = (i + 1) % max_sub_stream_num;
            }

        }

        internal int CreateSubStream(int sub_stream_id)
        {
            WaitBeforeLocking(new byte[1], 0x0f);
            lock (create_sub_stream_lock)
            {
                try
                {
                    int allocation_id = 0;
                    while (allocation_id < max_sub_stream_num)
                    {
                        if (sub_stream_id_list[allocation_id] == -1)
                        {
                            sub_stream_id_list[allocation_id] = sub_stream_id;
                            receiver_stream_unit_list[allocation_id] = new MemoryStream();
                            sender_stream_unit_list[allocation_id] = new MemoryStream();
                            return allocation_id;
                        }
                        allocation_id++;
                    }
                }
                catch
                {
                    return -1;
                }
            }

            return -1;

        }

        internal int CloseSubStream(int allocation_id)
        {
            WaitBeforeLocking(new byte[1], 0x0f);
            lock (create_sub_stream_lock)
            {
                try
                {
                    int t = sub_stream_id_list[allocation_id];

                    sub_stream_id_list[allocation_id] = -1;

                    read_interface_active_list[allocation_id] = false;
                    receiver_stream_unit_list[allocation_id].Close();
                    receiver_stream_unit_list[allocation_id] = null;
                    receiver_stream_unit_requester_index_list[allocation_id] = 0;
                    receiver_stream_unit_authority_index_list[allocation_id] = 0;

                    write_interface_active_list[allocation_id] = false;
                    sender_stream_unit_list[allocation_id].Close();
                    sender_stream_unit_list[allocation_id] = null;
                    write_request_length_list[allocation_id] = 0;

                    Console.WriteLine($"[ TSSS CLOSE   ] SUB STREAM = {t}");

                }
                catch
                {
                    return -1;
                }
            }

            return 0;

        }

        internal int GetIndex(int sub_stream_id)
        {
            WaitBeforeLocking(new byte[1], 0x0f);
            lock (create_sub_stream_lock)
            {
                try
                {
                    int i = 0;
                    while (i < max_sub_stream_num)
                    {
                        if (sub_stream_id_list[i] == sub_stream_id)
                        {
                            return i;
                        }
                        i++;
                    }
                }
                catch
                {
                    return -1;
                }
            }

            return -1;
        }

        internal void ActivateReadInterface(int allocation_id)
        {
            if (!read_interface_active_list[allocation_id])
            {
                read_interface_active_list[allocation_id] = true;
                WriteSwitchingTag(allocation_id);
            }
        }

        internal void DeactivateReadInterface(int allocation_id)
        {
            if (read_interface_active_list[allocation_id])
            {
                read_interface_active_list[allocation_id] = false;
                WriteSwitchingTag(allocation_id);
            }
        }

        private int WriteSwitchingTag(int allocation_id)
        {
            byte[] switching_tag = new byte[TAG_SIZE] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            Array.Copy(ConvertInt32To4ByteWord(sub_stream_id_list[allocation_id]), 0, switching_tag, 0, SUB_STREAM_ID_SIZE);
            WaitBeforeLocking(new byte[1], 0x0f);
            lock (sender_stream_unit_lock_list[allocation_id])
            {
                try
                {
                    sender_stream_unit_list[allocation_id].Position = write_request_length_list[allocation_id];
                    sender_stream_unit_list[allocation_id].Write(switching_tag, 0, TAG_SIZE);
                    write_request_length_list[allocation_id] = write_request_length_list[allocation_id] + TAG_SIZE;
                    Console.WriteLine($"[ TSSS WRITE   ] TAG INFO -> SUB STREAM = {sub_stream_id_list[allocation_id]}, PAYLOAD LENGTH = 0 [SWITCHING]");
                }
                catch
                {
                    return -1;
                }
            }

            return 0;

        }

        internal bool GetWriteInterfaceSwitch(int allocation_id)
        {
            return write_interface_active_list[allocation_id];
        }

        internal bool IsWriteBufferEmpty(int allocation_id)
        {
            return write_request_length_list[allocation_id] == 0;
        }

        internal int Write(int allocation_id, byte[] buffer, int buffer_start_index, int write_length)
        {
            if (write_interface_active_list[allocation_id])
            {
                try
                {
                    byte[] new_buffer = new byte[TAG_SIZE + write_length];
                    Array.Copy(ConvertInt32To4ByteWord(sub_stream_id_list[allocation_id]), 0, new_buffer, 0, SUB_STREAM_ID_SIZE);
                    Array.Copy(ConvertInt32To4ByteWord(write_length), 0, new_buffer, SUB_STREAM_ID_SIZE, PAYLOAD_LENGTH_SIZE);
                    Array.Copy(buffer, buffer_start_index, new_buffer, TAG_SIZE, write_length);

                    WaitBeforeLocking(new byte[1], 0x0f);
                    lock (sender_stream_unit_lock_list[allocation_id])
                    {
                        try
                        {
                            sender_stream_unit_list[allocation_id].Position = write_request_length_list[allocation_id];
                            sender_stream_unit_list[allocation_id].Write(new_buffer, buffer_start_index, new_buffer.Length);
                            write_request_length_list[allocation_id] = write_request_length_list[allocation_id] + TAG_SIZE + write_length;
                        }
                        catch
                        {
                            return -1;
                        }
                    }

                    string p = null;
                    for (int b = 0; b < write_length; b++)
                    {
                        string t = Convert.ToString(buffer[buffer_start_index + b], 16);
                        p = p + (t.Length == 2 ? t : "0" + t);
                    }
                    Console.WriteLine($"[ TSSS WRITE   ] TAG INFO -> SUB STREAM = {sub_stream_id_list[allocation_id]}, PAYLOAD LENGTH = {write_length} :: PAYLOAD -> {p}{(!read_interface_active_list[allocation_id] ? " [DROPED]" : "")}");

                }
                catch
                {
                    return -1;
                }
            }

            return 0;

        }

        internal int Read(int allocation_id, byte[] buffer, int buffer_start_index, int max_read_length, CancellationToken relay_cancellation)
        {
            byte[] wait_time = new byte[1];
            int read_size = 0;
            while (read_size == 0 && !cancellation.IsCancellationRequested && !relay_cancellation.IsCancellationRequested)
            {
                WaitBeforeLocking(wait_time, 0x0f);
                lock (receiver_stream_unit_lock_list[allocation_id])
                {
                    try
                    {
                        if (!(receiver_stream_unit_requester_index_list[allocation_id] == receiver_stream_unit_authority_index_list[allocation_id]))
                        {
                            receiver_stream_unit_list[allocation_id].Position = receiver_stream_unit_requester_index_list[allocation_id];
                            long readable_length = receiver_stream_unit_authority_index_list[allocation_id] - receiver_stream_unit_requester_index_list[allocation_id];
                            if (readable_length < max_read_length)
                            {
                                max_read_length = (int)readable_length;
                            }
                            read_size = receiver_stream_unit_list[allocation_id].Read(buffer, buffer_start_index, max_read_length);
                            receiver_stream_unit_requester_index_list[allocation_id] = receiver_stream_unit_requester_index_list[allocation_id] + read_size;
                            if (receiver_stream_unit_requester_index_list[allocation_id] == receiver_stream_unit_authority_index_list[allocation_id])
                            {
                                receiver_stream_unit_requester_index_list[allocation_id] = 0;
                                receiver_stream_unit_authority_index_list[allocation_id] = 0;
                            }
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            }

            if (read_size != 0 && !cancellation.IsCancellationRequested && !relay_cancellation.IsCancellationRequested)
            {
                return read_size;
            }

            return -1;

        }

        private void WaitBeforeLocking(byte[] wait_time_buffer, byte wait_time_law)
        {
            rnd.GetBytes(wait_time_buffer);
            Task.Delay(wait_time_buffer[0] & wait_time_law).Wait();
        }

        private static byte[] ConvertInt32To4ByteWord(int int32)
        {
            byte[] byte_4 = new byte[4]
            {
                (byte)((int32 >> 24) & 0xff), (byte)((int32 >> 16) & 0xff), (byte)((int32 >> 8) & 0xff), (byte)(int32 & 0xff)
            };
            return byte_4;
        }

        private static int Convert4ByteWordToInt32(byte[] byte_4)
        {
            int int32 = (byte_4[0] << 24) | (byte_4[1] << 16) | (byte_4[2] << 8) | byte_4[3];
            return int32;
        }

        internal void Display(int allocation_id)
        {
            byte[] buffer;
            Console.WriteLine($"SUB STREAM ID   {sub_stream_id_list[allocation_id]}");
            Console.WriteLine();
            Console.WriteLine("RECEIVER");
            Console.WriteLine($"    READ-IF SWITCH   :{read_interface_active_list[allocation_id]}");
            Console.WriteLine($"    REQUESTER INDEX  :{receiver_stream_unit_requester_index_list[allocation_id]}");
            Console.WriteLine($"    AUTHORITY INDEX  :{receiver_stream_unit_authority_index_list[allocation_id]}");
            Console.Write($"    BUFFER           :");
            buffer = receiver_stream_unit_list[allocation_id].ToArray();
            Dev.DisplayByteArray(buffer, buffer.Length);
            Console.WriteLine();
            Console.WriteLine("SENDER");
            Console.WriteLine($"    WRITE-IF SWITCH  :{write_interface_active_list[allocation_id]}");
            Console.WriteLine($"    WRITE-REQ LENGTH :{receiver_stream_unit_requester_index_list[allocation_id]}");
            Console.Write($"    BUFFER           :");
            buffer = sender_stream_unit_list[allocation_id].ToArray();
            Dev.DisplayByteArray(buffer, buffer.Length);
            Console.WriteLine();
        }

    }


    internal class TcpSubStream
    {
        private TSSS tsss;
        private int sub_stream_id;
        private int allocation_id;

        internal int SubStreamID
        {
            get { return sub_stream_id; }
        }

        internal int AllocationID
        {
            get { return allocation_id; }
        }

        internal TcpSubStream(TSSS tsss, int sub_stream_id)
        {
            this.tsss = tsss;
            this.sub_stream_id = sub_stream_id;
            allocation_id = tsss.GetIndex(sub_stream_id);
            if (allocation_id == -1)
            {
                allocation_id = tsss.CreateSubStream(sub_stream_id);
            }
        }

        ~TcpSubStream()
        {
            //Close(); これが謎Closeの原因か？
        }

        internal int ActivateReadInterface()
        {
            if (tsss.IsAvailable)
            {
                tsss.ActivateReadInterface(allocation_id);
                return 0;
            }

            return -1;
        }

        internal int DeactivateReadInterface()
        {
            if (tsss.IsAvailable)
            {
                tsss.DeactivateReadInterface(allocation_id);
                return 0;
            }

            return -1;
        }

        internal bool GetWriteInterfaceSwitch()
        {
            if (tsss.IsAvailable)
            {
                return tsss.GetWriteInterfaceSwitch(allocation_id);
            }

            return false;
        }

        internal void WaitWriteInterfaceActivation()
        {
            while (tsss.IsAvailable && !tsss.GetWriteInterfaceSwitch(allocation_id))
            {
                Task.Delay(1).Wait();
            }
        }

        internal void WaitWriteInterfaceDeactivation()
        {
            while (tsss.IsAvailable && tsss.GetWriteInterfaceSwitch(allocation_id))
            {
                Task.Delay(1).Wait();
            }
        }

        internal void WaitWriteBufferBecomingEmpty()
        {
            while (tsss.IsAvailable && !tsss.IsWriteBufferEmpty(allocation_id))
            {
                Task.Delay(1).Wait();
            }
        }

        internal int Write(byte[] buffer, int buffer_start_index, int write_length)
        {
            if (buffer.Length - buffer_start_index < write_length)
            {
                Console.WriteLine("Cannot write beyond the buffer length.");
                return -1;
                //throw new ArgumentException("Cannot write beyond the buffer length.");
            }

            if (tsss.IsAvailable)
            {
                return tsss.Write(allocation_id, buffer, buffer_start_index, write_length);
            }

            return -1;
        }

        internal int Read(byte[] buffer, int buffer_start_index, int max_read_length, CancellationToken relay_cancellation)
        {
            if (tsss.IsAvailable)
            {
                return tsss.Read(allocation_id, buffer, buffer_start_index, max_read_length, relay_cancellation);
            }

            return -1;
        }

        internal int Close()
        {
            if (tsss.IsAvailable)
            {
                return tsss.CloseSubStream(allocation_id);
            }

            return -1;
        }

        internal void Display()
        {
            tsss.Display(allocation_id);
        }

    }

}