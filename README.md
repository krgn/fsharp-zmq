# ZeroMQ sockets in FSharp

Using `ZeroMQ` socket in asynchronous computations in `FSharp` can be a daunting task. The reason
behind that is that `ZeroMQ` sockets are explicitly _not_ thread-safe. This is apparently about to
change:

<blockquote class="twitter-tweet" data-lang="en"><p lang="en" dir="ltr"><a href="https://twitter.com/krstngbbrt">@krstngbbrt</a> there&#39;s a new generation of socket types (client, server, radio, dish, scatter, gather) that are threadsafe.</p>&mdash; Pieter Hintjens (@hintjens) <a href="https://twitter.com/hintjens/status/757889447996391429">July 26, 2016</a></blockquote>
<script async src="//platform.twitter.com/widgets.js" charset="utf-8"></script>

Until these new, thread-safe socket types land in a proper release and get integrated into the
`.NET` `ZeroMQ` package I need a solution to allow me to pass around and use sockets on different
threads while still guaranteeing that all sockets get closed an disposed of properly.

The demo I cooked up in this repository makes heavy use of `AutoResetEvent` signaling and a single
locking step to synchronize yet another background thread which in turn owns the ZeroMQ socket. 

Take a [look at the source to see how its done.](https://github.com/krgn/fsharp-zmq/blob/master/zmq.tests/zmq.tests.fs)

# Usage

Build the thing using `Fake` like this:

```
./build.sh # or .\build.cmd on windows
```

Then start the program in your console and type-in how many random numbers you want to compute the
square of. Thats it. 
