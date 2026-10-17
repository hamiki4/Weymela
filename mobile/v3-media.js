/* Android-only approved-video download; private credentials stay native. */
(function(scope){
  "use strict";
  const path=/^\/api\/review-media\/ugc\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?:\/download)?$/i;
  function downloadUrl(href,origin){
    try{
      const url=new URL(href,origin);
      if(origin!=="https://localhost" || url.origin!==origin || url.username || url.password || url.search || url.hash || !path.test(url.pathname))return null;
      // Native validates the original URL again before attaching any cookies.
      if(String(href).includes("%") || /(?:^|\/)\.{1,2}(?:\/|$)/.test(String(href)))return null;
      return url.href;
    }catch{return null;}
  }
  function install(target){
    const cap=target.Capacitor;if(!cap || cap.getPlatform()!=="android")return;
    const pending=new WeakSet();
    target.document.addEventListener("click",event=>{
      const anchor=event.target?.closest?.("a[download]");
      if(!anchor || event.defaultPrevented || event.button!==0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey)return;
      const url=downloadUrl(anchor.getAttribute("href"),target.location.origin);if(!url)return;
      event.preventDefault();if(pending.has(anchor))return;pending.add(anchor);anchor.setAttribute("aria-busy","true");
      void cap.nativePromise("WeymelaMedia","save",{url}).catch(()=>{
        target.alert(target.document.documentElement.lang==="am"?"ቪዲዮውን ማስቀመጥ አልተቻለም። እንደገና ይሞክሩ።":"Couldn’t save the video. Try again.");
      }).finally(()=>{pending.delete(anchor);anchor.removeAttribute("aria-busy");});
    });
  }
  if(typeof module!=="undefined" && module.exports)module.exports={downloadUrl,install};
  else install(scope);
})(globalThis);
