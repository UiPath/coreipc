CD C:\Projects\CoreIpc\
FOR /L %%G IN (0,1,10) DO dotnet test --no-build -c Debug
PAUSE