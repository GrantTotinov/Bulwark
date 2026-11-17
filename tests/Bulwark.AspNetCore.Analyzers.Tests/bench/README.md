# Benchmark programs

The `Program.cs` files of the vulnerable and fixed apps from
[aspnetcore-security-bench](https://github.com/GrantTotinov/aspnetcore-security-bench) (MIT),
commit `45e03e59adc74761de7e96d3431de72fa55650f0`, report version 1.0.0,
DOI [10.5281/zenodo.23822216](https://doi.org/10.5281/zenodo.23822216).

They are copied unchanged so the tests run offline. `BenchTests` compiles each one with the Web SDK implicit usings
and checks that Bulwark reports the vulnerable program and nothing on the fixed one.

When the benchmark adds a pair, copy its two `Program.cs` files here as `<pair>/Vulnerable.cs` and
`<pair>/Fixed.cs` and add a row to `BenchTests`.

**The vulnerable programs are insecure on purpose.** Never copy their configuration into a real app.
