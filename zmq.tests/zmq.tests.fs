module zmq.tests

open ZeroMQ
open System
open System.Threading

//        _   _ _ _ _   _
//  _   _| |_(_) (_) |_(_) ___  ___
// | | | | __| | | | __| |/ _ \/ __|
// | |_| | |_| | | | |_| |  __/\__ \
//  \__,_|\__|_|_|_|\__|_|\___||___/

let unixtime (date: DateTime) =
  let ts = date.Ticks - (new DateTime(1970, 1, 1)).Ticks
  ts / TimeSpan.TicksPerMillisecond

let log tag msg =
  let now = DateTime.Now
  let tid = Thread.CurrentThread.ManagedThreadId
  printfn "[%d / %d / %s] %s" (unixtime now) tid tag msg

let withlog msg t =
  log "main" msg
  t

//  ____           ____
// |  _ \ ___ _ __/ ___| _ ____   __
// | |_) / _ \ '_ \___ \| '__\ \ / /
// |  _ <  __/ |_) |__) | |   \ V /
// |_| \_\___| .__/____/|_|    \_/
//           |_|

// single unit of work
let proc i =
  float i ** 2.0 |> int

// check if request/response are consistent
let isConsistent (i,res) =
  async {
    return sqrt (float res) = float i
  }

type RepSrv (addr: string) =

  let mutable run = true
  let mutable ctx: ZContext = null
  let mutable sock: ZSocket = null
  let mutable thread: Thread = null

  let mutable starter: AutoResetEvent = null
  let mutable stopper: AutoResetEvent = null

  let worker () =                       // worker function for processing requests
    if isNull ctx then                  // if the context is null, create it please
      ctx <- new ZContext()

    if isNull sock then                              // if the socket is null
      let socket = new ZSocket(ctx, ZSocketType.REP) // create it
      socket.SetOption(ZSocketOption.RCVTIMEO, 50) |> ignore // set this for the socket to periodically time out, such that the request loop can be interupted
      socket.Bind(addr)                                     // bind to address
      sock <- socket                                         // and safe for later use
      starter.Set() |> ignore                                // signal Start that startup is done

    while run do
      try
        let frame = sock.ReceiveFrame() // block to receive a request
        let number = frame.ReadInt32()  // read the number as int

        let reply = new ZFrame(proc number) // process the number and create a frame for response
        sock.Send(reply)                    // send response back

        frame.Dispose()                 // dispose of frame
        reply.Dispose()                 // dispose of reply
      with
        | :? ZException -> ()            // ReceiveFrame times out, so ignore the Exception

    sock.SetOption(ZSocketOption.LINGER, 0) |> ignore // loop exited, so set linger to 0
    sock.Close()                                     // and close socket
    sock.Dispose()                                   // dispose socket
    ctx.Dispose()                                    // now context can also be disposed
    stopper.Set() |> ignore                           // signal that Stop is done

  do
    starter <- new AutoResetEvent(false) // initialise the signals
    stopper <- new AutoResetEvent(false)

  member self.Stop () =
    run <- false                         // break the loop by setting this to false
    stopper.WaitOne() |> ignore          // wait for the signal that stopping is done

  member self.Start () =
    thread <- new Thread(new ThreadStart(worker)) // create a worker thread and save for later use
    thread.Start()                               // start the thread
    starter.WaitOne() |> ignore                   // wait until startup was signaled done

//  ____             ____ _ _            _
// |  _ \ ___  __ _ / ___| (_) ___ _ __ | |_
// | |_) / _ \/ _` | |   | | |/ _ \ '_ \| __|
// |  _ <  __/ (_| | |___| | |  __/ | | | |_
// |_| \_\___|\__, |\____|_|_|\___|_| |_|\__|
//               |_|

type ReqClient(addr: string) =

  let mutable starter:   AutoResetEvent = null
  let mutable stopper:   AutoResetEvent = null
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

  let worker _ =                        // function to hold the client socket
    if isNull ctx then                  // if not yet present,
      ctx <- new ZContext()              // initialise the ZeroMQ context for this thread

    if isNull sock then                                       // if not yet present
      let socket = new ZSocket(ctx, ZSocketType.REQ)          // initialise the socket
      socket.SetOption(ZSocketOption.RCVTIMEO, 1000) |> ignore // set receive timeout
      socket.Connect(addr)                                    // connect to server
      sock <- socket                                           // and safe for later use
      starter.Set() |> ignore                                  // signal `starter` that startup is done

    while run do                        // run for as long as `run` is set to `true`
      try
        requester.WaitOne() |> ignore    // wait for the signal that a new request is ready (or shutdown is reuqested)
        if run then                     // `run` is usually true, but shutdown first sets this to false to exit the loop
          let frame = new ZFrame(request) // create a new ZFrame to send
          sock.Send(frame)                // and send it via sock
          let reply = sock.ReceiveFrame() // block and wait for reply frame
          response <- reply.ReadInt32()    // read and save the response globally
          responder.Set() |> ignore        // signal that the response is ready to be consumed
          frame.Dispose()                 // dispose of frame
          reply.Dispose()                 // and reply
      with
        | :? ZException as exn ->        // TIMEOUT might be triggered here
          sprintf "Request failed: %s" exn.ErrText
          |> log "client"

    sock.SetOption(ZSocketOption.LINGER, 0) |> ignore // set linger to 0 to close socket quickly
    sock.Close()                                     // close the socket
    sock.Dispose()                                   // dispose of it
    ctx.Dispose()                                    // dispose of the context (should not hang now)
    stopper.Set() |> ignore                           // signal that everything was cleaned up now

  do
    starter   <- new AutoResetEvent(false) // initialize the signals
    stopper   <- new AutoResetEvent(false)
    requester <- new AutoResetEvent(false)
    responder <- new AutoResetEvent(false)

  member self.Start() =
    thread <- new Thread(new ThreadStart(worker)) // create a new Thread as context for the client socket
    thread.Start()                               // start that thread, causing the socket to be intitialized
    starter.WaitOne() |> ignore                   // wait for the `starter` signal to indicate startup is done

  member self.Stop() =
    run <- false                         // stop the loop from iterating
    requester.Set() |> ignore            // but signal requester one more time to exit loop
    stopper.WaitOne() |> ignore          // wait for the stopper to signal that everything was disposed

  member self.Request(req: int) : int = // synchronously request the square of `req`
    lock lokk  (fun _ ->                   // lock on the `lokk` object while executing this transaction
      request <- req                     // first set the requets
      requester.Set() |> ignore          // then signal a request is ready for execution
      responder.WaitOne() |> ignore      // wait for signal from the responder that execution has finished
      response)                         // return the response

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

  let server = new RepSrv("tcp://*:5555")            // instantiate a server
  let client = new ReqClient("tcp://127.0.0.1:5555") // instantiate a client socket

  server.Start()                        // synchronous
  client.Start()                        // synchronous

  while cmd <> "stop" do
    cmd <- Console.ReadLine()
    if cmd <> "gc" then
      try
        num <- int cmd
        let nums = [                    // construct a list of random numbers to compute results
          for n in 1 .. num do
            yield rand.Next(0, 256)
          ]

        sprintf "requesting results for %d items" num
        |> log "main"

        let request n  =                // asynchronous request function using `client`
          async {
            let res = client.Request(n) // synchronously get the result for `n`
            return (n, res)             // and combine `n` with its result
          }

        List.map request nums           // create a list of async computations
        |> Async.Parallel                // convert into a parallel computation
        |> Async.RunSynchronously        // and run that synchronously, returning an array of tuples
        |> withlog "requests done"
        |> Array.map isConsistent        // construct a list of async consistency checks for each result
        |> Async.Parallel                // turn that into a parallel computation
        |> Async.RunSynchronously        // and run it, returning an array of boolean values
        |> withlog "consistency check done"
        |> Array.fold (fun m v -> if not m then m else v) true // fold that into a global answer whether all
        |> sprintf "results ok: %b"                       // all responses correlate to their request
        |> log "main"
      with
        | _ -> ()
    else
      let before = GC.GetTotalMemory(false)
      GC.Collect()
      let after = GC.GetTotalMemory(true)
      sprintf "[%A before] [%A after] [%A freed]" before after (before - after)
      |> log "main"

  client.Stop()                         // synchronous
  server.Stop()                         // synchronous

  0
