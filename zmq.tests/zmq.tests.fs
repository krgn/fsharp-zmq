module zmq.tests

open ZeroMQ
open System
open System.Threading

let unixtime (date: DateTime) =
  let ts = date.Ticks - (new DateTime(1970, 1, 1)).Ticks
  ts / TimeSpan.TicksPerMillisecond

let log tag msg =
  let now = DateTime.Now
  let tid = Thread.CurrentThread.ManagedThreadId
  printfn "[%d / %d / %s] %s" (unixtime now) tid tag msg

//  ____           ____
// |  _ \ ___ _ __/ ___| _ ____   __
// | |_) / _ \ '_ \___ \| '__\ \ / /
// |  _ <  __/ |_) |__) | |   \ V /
// |_| \_\___| .__/____/|_|    \_/
//           |_|

type RepSrv (addr: string) =

  let mutable run = true
  let mutable ctx : ZContext = null
  let mutable sock : ZSocket = null
  let mutable thread : Thread = null

  let proc i =
    float i ** 2.0 |> int

  let worker () =
    log "server" "start"

    if isNull ctx then
      ctx <- new ZContext()

    if isNull sock then
      log "server" "initializing"
      let socket = new ZSocket(ctx, ZSocketType.REP)
      socket.SetOption(ZSocketOption.RCVTIMEO, 50) |> ignore
      socket.Bind(addr)
      sock <- socket

    while run do
      try
        let frame = sock.ReceiveFrame()
        let number = frame.ReadInt32()

        let reply = new ZFrame(proc number)
        sock.Send(reply)

        frame.Dispose()
        reply.Dispose()
      with
        | :? ZException -> ()

    sock.SetOption(ZSocketOption.LINGER, 0) |> ignore
    sock.Close()
    sock.Dispose()
    ctx.Dispose()
    log "server" "done"

  member self.Stop () =
    log "server" "stop"
    run <- false

  member self.Start () =
    let t = new Thread(new ThreadStart(worker))
    t.Start()
    thread <- t

//  ____             ____ _ _            _
// |  _ \ ___  __ _ / ___| (_) ___ _ __ | |_
// | |_) / _ \/ _` | |   | | |/ _ \ '_ \| __|
// |  _ <  __/ (_| | |___| | |  __/ | | | |_
// |_| \_\___|\__, |\____|_|_|\___|_| |_|\__|
//               |_|

type ReqClient(addr: string) =

  let mutable requester: AutoResetEvent = null
  let mutable responder: AutoResetEvent = null

  let mutable request  : int = 0
  let mutable response : int = 0

  let mutable thread = null
  let mutable ctx = null
  let mutable run = true
  let mutable sock = null

  let lokk = new Object()
  let rand = new Random()

  let worker _ =
    if isNull ctx then
      ctx <- new ZContext()

    if isNull sock then
      let socket = new ZSocket(ctx, ZSocketType.REQ)
      socket.SetOption(ZSocketOption.RCVTIMEO, 1000) |> ignore
      socket.Connect(addr)
      sock <- socket

    while run do
      try
        requester.WaitOne()
        if run then
          let frame = new ZFrame(request)
          sock.Send(frame)
          let reply = sock.ReceiveFrame()
          response <- reply.ReadInt32()
          responder.Set() |> ignore
          frame.Dispose()
          reply.Dispose()
      with
        | :? ZException as exn ->
          sprintf "Request failed: %s" exn.ErrText
          |> log "client"

    sock.SetOption(ZSocketOption.LINGER, 0) |> ignore
    sock.Close()
    sock.Dispose()
    ctx.Dispose()
    log "client" "done"

  do
    requester <- new AutoResetEvent(false)
    responder <- new AutoResetEvent(false)

  member self.Start() =
    let t = new Thread(new ThreadStart(worker))
    t.Start()
    thread <- t

  member self.Stop() =
    log "client" "stop"
    run <- false
    requester.Set()

  member self.Request(req: int) : int =
    lock lokk  (fun _ ->
      request <- req
      requester.Set()
      responder.WaitOne()
      response)

//                  _
//  _ __ ___   __ _(_)_ __
// | '_ ` _ \ / _` | | '_ \
// | | | | | | (_| | | | | |
// |_| |_| |_|\__,_|_|_| |_|

[<EntryPoint>]
let main argv =
  let rand = new Random()
  let mutable cmd : string = null
  let mutable num : int = 0

  let srv = new RepSrv("tcp://*:5555")
  srv.Start()

  Thread.Sleep(1000)

  let client = new ReqClient("tcp://127.0.0.1:5555")
  client.Start()

  while cmd <> "stop" do
    cmd <- Console.ReadLine()
    try
      let num = int cmd
      sprintf "requesting: %d" num
      |> log "client"

      client.Request(num)
      |> sprintf "response: %d"
      |> log "client"
    with
      | _ -> ()

  log "main" "stop worker"

  client.Stop()
  srv.Stop()

  Thread.Sleep(1000)

  0
