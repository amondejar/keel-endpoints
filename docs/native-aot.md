# Native AOT

```xml
<PublishAot>true</PublishAot>
```

## How it works

The generated `app.MapKeelEndpoints(prefix)` is one typed `MapMethods` call per endpoint class, with no
reflection. ASP.NET Core's Request Delegate Generator, which makes Minimal API binding AOT-safe, cannot see
code written by another generator, so with `PublishAot` the package builds in two steps: it writes the
registration to disk, then compiles it as ordinary source. The Request Delegate Generator generates binding
for every endpoint and the validation generator finds every contract. `KeelRegistrationFile=true|false`
forces the two-step build on or off.

## What you still do

- List every type on the wire in a `JsonSerializerContext` and insert it into the HTTP JSON options, as in
  any AOT Minimal API (`samples/Keel.AotSample`, or `dotnet new keel-api --aot`).
- Declare binding with `[BindRequest]` and the stream format with `[ServerSentEvents]`, on the contract and
  the class. Setting `options.Binding` or `options.StreamFormat` in `Configure` to something else fails at
  startup: the generated code was decided at compile time.
- Use the generated `MapKeelEndpoints(prefix)`. The reflective `MapKeelEndpoints(assembly, prefix)` and
  `MapEndpoint` are marked `[RequiresUnreferencedCode]`.

## Checked in CI

CI publishes `samples/Keel.AotSample` natively with every trimming and AOT warning as an error, runs it and
calls each endpoint (`eng/aot-smoke.sh`), and publishes the `keel-api --aot` template the same way
(`eng/template-smoke.sh`).
