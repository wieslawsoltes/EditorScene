// Synthetic protocol peer for integration tests, not a production language server.
// It receives real .NET ClientWebSocket messages and validates synchronization.
import assert from 'node:assert/strict';
import {WebSocketServer} from 'ws';

export function createLspFixture(server) {
  const websocket = new WebSocketServer({noServer:true,maxPayload:1024*1024,perMessageDeflate:false});
  const journal=[], errors=[], waiters=[];
  const sessions=[];
  const record=(method,params)=>{
    journal.push({method,params});
    for(const waiter of [...waiters]) if(waiter.method===method){clearTimeout(waiter.timer);waiters.splice(waiters.indexOf(waiter),1);waiter.resolve(journal.at(-1));}
  };
  const upgrade=(request,socket,head)=>{
    if(new URL(request.url,'http://localhost').pathname!=='/EditorScene/lsp-test'){socket.destroy();return;}
    websocket.handleUpgrade(request,socket,head,client=>websocket.emit('connection',client,request));
  };
  server.on('upgrade',upgrade);
  const position=(text,offset)=>{
    assert.ok(offset>=0&&offset<=text.length);let line=0,start=0;
    for(let i=0;i<offset;i++){if(text[i]==='\r'){if(text[i+1]==='\n')i++;line++;start=i+1;}else if(text[i]==='\n'){line++;start=i+1;}}
    return {line,character:offset-start};
  };
  const offset=(text,pos)=>{
    assert.ok(Number.isInteger(pos.line)&&Number.isInteger(pos.character)&&pos.line>=0&&pos.character>=0);
    let line=0,start=0;
    for(let i=0;i<text.length&&line<pos.line;i++){if(text[i]==='\r'){if(text[i+1]==='\n')i++;line++;start=i+1;}else if(text[i]==='\n'){line++;start=i+1;}}
    assert.equal(line,pos.line);let end=start;while(end<text.length&&!/[\r\n]/.test(text[end]))end++;
    assert.ok(start+pos.character<=end,'Invalid UTF-16 position from client');return start+pos.character;
  };
  const range=(text,start,end)=>({start:position(text,start),end:position(text,end)});
  websocket.on('connection',socket=>{
    const session={uri:null,text:'',version:0,closed:false};sessions.push(session);
    const reply=(id,result)=>socket.send(JSON.stringify({jsonrpc:'2.0',id,result}));
    socket.on('message',bytes=>{
      let message;
      try {
        message=JSON.parse(bytes.toString());assert.equal(message.jsonrpc,'2.0');
        if(!message.method){record('serverRequestResult',message);assert.equal(message.id,'reject-workspace-edit');assert.equal(message.error?.code,-32601);return;}
        const {id,method,params:p}=message;
        record(method,p);
        switch(method){
          case 'initialize':
            assert.deepEqual(p.capabilities.general.positionEncodings,['utf-16']);
            assert.equal(p.capabilities.workspace.applyEdit,false);
            reply(id,{capabilities:{positionEncoding:'utf-16',textDocumentSync:{openClose:true,change:2,save:{includeText:true}},
              completionProvider:{triggerCharacters:['.']},hoverProvider:true,signatureHelpProvider:{triggerCharacters:['(']},
              documentSymbolProvider:true,foldingRangeProvider:true,documentFormattingProvider:true,
              diagnosticProvider:{identifier:'fixture',interFileDependencies:false,workspaceDiagnostics:false},
              semanticTokensProvider:{full:true,legend:{tokenTypes:['keyword','class'],tokenModifiers:[]}}}});break;
          case 'initialized':break;
          case 'textDocument/didOpen':
            assert.equal(session.uri,null);session.uri=p.textDocument.uri;session.text=p.textDocument.text;session.version=p.textDocument.version;assert.equal(session.version,1);break;
          case 'textDocument/didChange':
            assert.equal(p.textDocument.uri,session.uri);assert.equal(p.textDocument.version,session.version+1);
            assert.equal(p.contentChanges.length,1);for(const change of p.contentChanges){assert.ok(change.range,'Incremental sync must include a range');const start=offset(session.text,change.range.start),end=offset(session.text,change.range.end);assert.ok(end>=start);session.text=session.text.slice(0,start)+change.text+session.text.slice(end);}
            session.version=p.textDocument.version;break;
          case 'textDocument/documentSymbol':{
            const name=session.text.indexOf('Demo');reply(id,name<0?[]:[{name:'RemoteDemo',kind:5,range:range(session.text,0,session.text.length),selectionRange:range(session.text,name,name+4),children:[]}]);break;
          }
          case 'textDocument/foldingRange':{
            const first=session.text.indexOf('{'),last=session.text.lastIndexOf('}');reply(id,first<0||last<first?[]:[{startLine:position(session.text,first).line,endLine:position(session.text,last).line,kind:'region'}]);break;
          }
          case 'textDocument/semanticTokens/full':{
            const tokens=[];const keyword=session.text.indexOf('class'),name=session.text.indexOf('Demo');if(keyword>=0)tokens.push([keyword,5,0]);if(name>=0)tokens.push([name,4,1]);
            let prevLine=0,prevChar=0;const data=[];for(const [start,length,kind] of tokens){const pos=position(session.text,start);data.push(pos.line-prevLine,pos.line===prevLine?pos.character-prevChar:pos.character,length,kind,0);prevLine=pos.line;prevChar=pos.character;}
            reply(id,{data});break;
          }
          case 'textDocument/diagnostic':{
            assert.equal(p.identifier,'fixture');const resultId='v'+session.version;
            if(p.previousResultId===resultId)reply(id,{kind:'unchanged',resultId});
            else {const name=session.text.indexOf('Demo');reply(id,{kind:'full',resultId,items:name<0?[]:[{range:range(session.text,name,name+4),severity:2,code:'LSP001',message:'Diagnostic supplied by the WebSocket fixture'}]});}break;
          }
          case 'textDocument/completion':{
            const caret=offset(session.text,p.position);let start=caret,end=caret;
            while(start>0&&/[\w]/.test(session.text[start-1]))start--;while(end<session.text.length&&/[\w]/.test(session.text[end]))end++;
            assert.equal(session.text.slice(start,end),'LisSuffix');
            reply(id,{isIncomplete:false,items:[{label:'List',kind:7,sortText:'0',detail:'Remote generic list',documentation:{kind:'plaintext',value:'A completion supplied over the real WebSocket.'},
              textEdit:{newText:'List<int>',insert:range(session.text,start,caret),replace:range(session.text,start,end)},
              additionalTextEdits:[{range:range(session.text,0,0),newText:'using System.Collections.Generic;\n'}]}]});break;
          }
          case 'textDocument/hover':reply(id,{contents:{kind:'plaintext',value:'LSP: Demo from the test language server'}});break;
          case 'textDocument/signatureHelp':reply(id,{signatures:[{label:'RemoteCall(int value)',parameters:[{label:[11,20]}],documentation:'Remote overload'}],activeSignature:0,activeParameter:0});break;
          case 'textDocument/formatting':reply(id,[{range:range(session.text,0,session.text.length),newText:session.text.replace('List<int>','List<int> values;')}]);break;
          case 'textDocument/didSave':assert.equal(p.textDocument.uri,session.uri);assert.equal(p.text,session.text);break;
          case 'textDocument/didClose':assert.equal(p.textDocument.uri,session.uri);session.closed=true;break;
          case 'shutdown':assert.ok(session.closed);reply(id,null);break;
          case 'exit':record('sessionEnded',{text:session.text,version:session.version});socket.close();break;
          case '$/cancelRequest':break;
          default:if(id!==undefined)socket.send(JSON.stringify({jsonrpc:'2.0',id,error:{code:-32601,message:'Not implemented by test fixture'}}));
        }
      }catch(error){errors.push(error.stack);if(message?.id!==undefined&&socket.readyState===1)socket.send(JSON.stringify({jsonrpc:'2.0',id:message.id,error:{code:-32603,message:error.message}}));}
    });
    socket.on('error',error=>errors.push(error.message));
  });
  return {
    journal,errors,sessions,
    waitFor(method){const found=journal.find(x=>x.method===method);if(found)return Promise.resolve(found);return new Promise((resolve,reject)=>{const waiter={method,resolve,timer:setTimeout(()=>{waiters.splice(waiters.indexOf(waiter),1);reject(new Error('Missing LSP message: '+method));},10000)};waiters.push(waiter);});},
    rejectWorkspaceEdit(){for(const socket of websocket.clients)socket.send(JSON.stringify({jsonrpc:'2.0',id:'reject-workspace-edit',method:'workspace/applyEdit',params:{edit:{changes:{'file:///not-opened.txt':[{range:{start:{line:0,character:0},end:{line:0,character:0}},newText:'MUST NOT APPLY'}]}}}}));},
    async close(){server.off('upgrade',upgrade);for(const socket of websocket.clients)socket.terminate();await new Promise(resolve=>websocket.close(resolve));}
  };
}
