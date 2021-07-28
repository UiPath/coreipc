CD C:\Projects\CoreIpc\
FOR /L %%G IN (0,1,10) DO (
    echo RUN %%G 
    dotnet test --no-build -c Debug
)
PAUSE