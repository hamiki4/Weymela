package com.weymela.app;

import android.webkit.CookieManager;
import android.webkit.ServiceWorkerClient;
import android.webkit.ServiceWorkerController;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebView;
import com.getcapacitor.Bridge;
import com.getcapacitor.BridgeWebViewClient;
import java.io.ByteArrayInputStream;
import java.net.HttpURLConnection;
import java.util.Collections;
import java.util.Map;

final class PrivateMediaWebViewClient extends BridgeWebViewClient {
    private final String userAgent;
    PrivateMediaWebViewClient(Bridge bridge) {
        super(bridge);userAgent=bridge.getWebView().getSettings().getUserAgentString();
        ServiceWorkerController.getInstance().setServiceWorkerClient(new ServiceWorkerClient(){
            @Override public WebResourceResponse shouldInterceptRequest(WebResourceRequest request){
                WebResourceResponse response=privateResponse(request);
                return response!=null?response:bridge.getLocalServer().shouldInterceptRequest(request);
            }
        });
    }
    @Override public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
        WebResourceResponse response=privateResponse(request);
        return response!=null?response:super.shouldInterceptRequest(view,request);
    }
    private WebResourceResponse privateResponse(WebResourceRequest request){
        String local=request.getUrl().toString();
        if(ProductionMediaPolicy.production(local,false)==null)return null;
        if(!"GET".equals(request.getMethod()) || request.isForMainFrame())return unavailable();
        try {
            String range=null;
            for(Map.Entry<String,String> header:request.getRequestHeaders().entrySet())
                if(header.getKey().equalsIgnoreCase("Range"))range=header.getValue();
            String cookie=CookieManager.getInstance().getCookie(ProductionMediaPolicy.production(local,false).toString());
            PrivateMediaTransport.Response response=PrivateMediaTransport.open(local,false,cookie,userAgent,range,
                uri -> (HttpURLConnection)uri.toURL().openConnection());
            return new WebResourceResponse(response.type,null,response.status,response.status==206?"Partial Content":"OK",response.headers,response.body);
        } catch(Exception ignored){return unavailable();}
    }
    private static WebResourceResponse unavailable(){
        return new WebResourceResponse("text/plain","UTF-8",403,"Unavailable",Collections.singletonMap("Cache-Control","no-store"),new ByteArrayInputStream(new byte[0]));
    }
}
