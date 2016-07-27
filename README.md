# ZeroMQ sockets in FSharp

Using `ZeroMQ` socket in asynchronous computations in `FSharp` can be a daunting task. The reason
behind that is that `ZeroMQ` sockets are explicitly _not_ thread-safe. This is apparently about to
change:

<blockquote class="twitter-tweet" data-lang="en"><p lang="en" dir="ltr"><a href="https://twitter.com/krstngbbrt">@krstngbbrt</a> there&#39;s a new generation of socket types (client, server, radio, dish, scatter, gather) that are threadsafe.</p>&mdash; Pieter Hintjens (@hintjens) <a href="https://twitter.com/hintjens/status/757889447996391429">July 26, 2016</a></blockquote>
<script async src="//platform.twitter.com/widgets.js" charset="utf-8"></script>

Until these new, thread-safe socket types land in a proper release and get integrated into the
`.NET` `ZeroMQ` package I need a solution to allow me to pass around and use sockets on different
threads while still guaranteeing that all sockets get closed an disposed properly.

The solution I cooked up in this repository makes use of `AutoResetEvent` signaling and a single
locking step to synchronize yet another background thread which in turn owns the ZeroMQ socket.
