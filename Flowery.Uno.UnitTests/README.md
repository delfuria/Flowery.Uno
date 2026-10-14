## Flowery.Uno.UnitTests

### Run locally

The project targets `net10.0-desktop` (Skia, any OS) and `net10.0-windows10.0.19041` (WinUI, Windows only)
and uses the Microsoft.Testing.Platform runner, so run it with `dotnet run`:

```bash
dotnet run --project Flowery.Uno.UnitTests -f net10.0-desktop
dotnet run --project Flowery.Uno.UnitTests -f net10.0-windows10.0.19041
```

`dotnet test` is not supported: on the .NET 10 SDK it fails with
"Testing with VSTest target is no longer supported by Microsoft.Testing.Platform".
