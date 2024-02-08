echo Rebuilding @uipath/coreipc and @uipath/coreipc-web
call npm run build

cd ./dist/prepack/node
echo Unpublishing @uipath/coreipc
call npm unpublish --force --registry http://localhost:4873/
echo Republishing @uipath/coreipc
call npm publish --registry http://localhost:4873/

echo Doing it
cd ../web
echo Unpublishing @uipath/coreipc-web
call npm unpublish --force --registry http://localhost:4873/
echo Republishing @uipath/coreipc-web
call npm publish --registry http://localhost:4873/

cd ../..
