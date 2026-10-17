import test from 'node:test';import assert from 'node:assert/strict';import {createRequire} from 'node:module';
const require=createRequire(import.meta.url);const {downloadUrl,install}=require('../v3-media.js');
const path='/api/review-media/ugc/12345678-abcd-4321-9876-123456789abc/download';
function app(nativePromise,platform='android',lang='en'){
 let click;const alerts=[];const attributes=new Map([['href',path],['download','']]);
 const anchor={getAttribute:k=>attributes.get(k),setAttribute:(k,v)=>attributes.set(k,v),removeAttribute:k=>attributes.delete(k)};
 const target={Capacitor:{getPlatform:()=>platform,nativePromise},location:{origin:'https://localhost'},document:{documentElement:{lang},addEventListener:(type,fn)=>{assert.equal(type,'click');click=fn;}},alert:message=>alerts.push(message)};
 install(target);const event=()=>({target:{closest:()=>anchor},button:0,defaultPrevented:false,preventDefault(){this.defaultPrevented=true;}});
 return {target,anchor,attributes,alerts,event,click:()=>click};
}
const settle=()=>new Promise(resolve=>setImmediate(resolve));
test('maps only approved local video download URLs',()=>{
 assert.equal(downloadUrl(path,'https://localhost'),'https://localhost'+path);
 assert.equal(downloadUrl(path.replace('/download',''),'https://localhost'),'https://localhost'+path.replace('/download',''));
 for(const bad of ['https://evil.invalid'+path,'https://api.weymela.com'+path,path+'?token=x',path+'#fragment',path.replace('ugc/','ugc/../ugc/'),path.replace('ugc','%75gc'),'/api/session',path.replace('/ugc/','/promotions/')])assert.equal(downloadUrl(bad,'https://localhost'),null,bad);
 assert.equal(downloadUrl(path,'https://weymela.com'),null);
});
test('leaves browser downloads and modified clicks unchanged',()=>{
 const browser=app(()=>assert.fail('No native request'),'web');assert.equal(browser.click(),undefined);
 const native=app(()=>assert.fail('No native request'));const event=native.event();event.ctrlKey=true;native.click()(event);assert.equal(event.defaultPrevented,false);
});
test('uses native file picker once and shows inline busy state until resolved',async()=>{
 let resolve,calls=0;const native=app((plugin,method,input)=>{calls++;assert.equal(plugin,'WeymelaMedia');assert.equal(method,'save');assert.deepEqual(input,{url:'https://localhost'+path});return new Promise(done=>resolve=done);});
 const first=native.event();native.click()(first);assert.equal(first.defaultPrevented,true);assert.equal(native.attributes.get('aria-busy'),'true');native.click()(native.event());assert.equal(calls,1);
 resolve({saved:true});await settle();assert.equal(native.attributes.has('aria-busy'),false);assert.deepEqual(native.alerts,[]);
});
test('cancelled save creates no false success or error message',async()=>{
 const native=app(async()=>({saved:false}));native.click()(native.event());await settle();assert.deepEqual(native.alerts,[]);assert.equal(native.attributes.has('aria-busy'),false);
});
test('failed save clears progress and gives a short localized error',async()=>{
 for(const lang of ['en','am']){const native=app(async()=>{throw new Error('No private cookies or technical error details should be exposed');},'android',lang);native.click()(native.event());await settle();assert.equal(native.attributes.has('aria-busy'),false);assert.equal(native.alerts.length,1);assert(!native.alerts[0].includes('cookies'));if(lang==='am')assert(native.alerts[0].includes('ቪዲዮ'));}
});
