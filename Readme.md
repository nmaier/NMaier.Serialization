# A (somewhat more) efficient binary formatter  #

Because serialization is hard 

## Goals ##

* Be compatible enough with built-in C# Serialization, while still being efficient enough in the CPU, memory and storage size departments
	* Serialize most things incl. `ISerializable`
	* Omit feature I do not need, in particular, surrugates, events
* Provide an efficient (enough) binary representation that is still reliable (for persistence)
	* Use basic tags (`ElementType`) to mark in a stream what comes next
	* Provide specialized tags for things like `null`, PODs (including DateTime),  enums, different arrays (empty, byte, 1-item, rank-1, PODs, generic objects), etc.
	* Allow cycles and copies by writing an object only once and writing references to that afterwards
	* Avoid duplicate strings
	* Tune writing of (`unsigend`) lengths (inspired by websocket frames)
		* &lt; 254 will use one byte
		* &lt;= `ushort.MAXVALUE` will use three bytes (16-bit + one byte for the  tag (`0xfe`)
		* Otherwise use 5 bytes (32-bit + one byte for the tag (`0xff`))
		* Since most lengths written will be shortish, most will only use 1 byte instead of 4 bytes for an `int`.
	* Optimize assembly/type storage a bit (See also `EfficientSerializationBinder.RegisterKnown`)
* Thread-safe, reusable `IFormatter` (allowing to specify the `StreamingContext` per `Serialize`/`Deserialize` call)
	* Make use of `Concurrent` for global caches
* Be somewhat hackable (e.g. to add new optimized representations without breaking existing serialized representations)


## Non-Goals ##

* Be entirely compatible
* Be secure (especially against tempering)
* Be error-resilient/use advanced error detection (against stream errors)
* Be memory efficient in the face of serializing tons of different object types (the caches are unlimited in growth by design)

## State ##

**Very** *alpha*! You have been warned!

*Good enough* for caching stuff to disk (that is, you can regenerate things at any time)

Do **not** use this over the network unless you bolt message authentication on top.
Or else an attacker will literally delete all your stuff!

Probably **not** a great idea to use this inter-application, either.

## TODO ##

- Optimize list types
- Optimize dict types