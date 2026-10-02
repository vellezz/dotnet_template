# tools/packages

Lokalne źródło pakietów NuGet z narzędziem deweloperskim `SuperApp.Cli` (`dotnet superapp`, ADR-0046).

Pakiety (`*.nupkg`) nie są wersjonowane: tworzy je `tools/bootstrap.ps1` / `tools/bootstrap.sh`. Katalog musi istnieć, bo jest
źródłem w `nuget.config`.
