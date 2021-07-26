CD C:\Projects\CoreIpc\src\UiPath.CoreIpc.Tests
FOR /L %%G IN (0,1,10) DO dotnet test --no-build -c Debug
PAUSE