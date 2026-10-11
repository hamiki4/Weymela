package com.weymela.app;

import android.webkit.CookieManager;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.getcapacitor.plugin.util.HttpRequestHandler;
import java.io.IOException;
import java.net.CookieHandler;
import java.net.URI;
import java.util.Collections;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import org.json.JSONObject;

@CapacitorPlugin(name = "WeymelaApi")
public class WeymelaApi extends Plugin {
    private final ExecutorService executor = Executors.newFixedThreadPool(4);
    private String userAgent;
    @Override public void load() {
        userAgent = getBridge().getWebView().getSettings().getUserAgentString();
        CookieManager.getInstance().setAcceptCookie(true);
        CookieHandler.setDefault(new CookieHandler() {
            @Override public Map<String,List<String>> get(URI uri, Map<String,List<String>> headers) throws IOException {
                if (!ProductionApiPolicy.allows(uri.toString())) return Collections.emptyMap();
                String cookie = CookieManager.getInstance().getCookie(uri.toString());
                return cookie == null || cookie.isEmpty() ? Collections.emptyMap() : Collections.singletonMap("Cookie", Collections.singletonList(cookie));
            }
            @Override public void put(URI uri, Map<String,List<String>> headers) throws IOException {
                if (!ProductionApiPolicy.allows(uri.toString())) return;
                for (Map.Entry<String,List<String>> entry : headers.entrySet()) {
                    if (entry.getKey() != null && entry.getKey().equalsIgnoreCase("Set-Cookie"))
                        for (String value : entry.getValue()) CookieManager.getInstance().setCookie(uri.toString(), value);
                }
                CookieManager.getInstance().flush();
            }
        });
    }
    @PluginMethod public void request(PluginCall call) {
        String url = call.getString("url", "");
        String method = call.getString("method", "GET");
        if (!ProductionApiPolicy.allows(url) || !(method.equals("GET") || method.equals("POST"))) {
            call.reject("Unsupported API request."); return;
        }
        JSObject headers = call.getObject("headers", new JSObject());
        Iterator<String> keys = headers.keys();
        while (keys.hasNext()) if (!ProductionApiPolicy.headerAllowed(keys.next())) {
            call.reject("Unsupported API header."); return;
        }
        headers.put("X-Weymela-Request", "1");
        headers.put("Cache-Control", "no-store");
        headers.put("User-Agent", userAgent);
        call.getData().put("headers", headers);
        call.getData().put("disableRedirects", true);
        call.getData().put("connectTimeout", 30000);
        call.getData().put("readTimeout", 120000);
        call.getData().put("responseType", "arraybuffer");
        executor.execute(() -> {
            try {
                JSObject result = HttpRequestHandler.request(call, method, getBridge());
                if (result.getInt("status") >= 300 && result.getInt("status") < 400) {
                    call.reject("API redirects are unavailable."); return;
                }
                JSONObject raw = result.getJSONObject("headers");
                JSObject safe = new JSObject();
                Iterator<String> names = raw.keys();
                while (names.hasNext()) {
                    String name = names.next();
                    if (ProductionApiPolicy.responseHeaderAllowed(name)) safe.put(name, raw.get(name));
                }
                result.put("headers", safe);
                call.resolve(result);
            } catch (Exception ignored) { call.reject("The secure API request could not be completed."); }
        });
    }
    @Override protected void handleOnDestroy() { executor.shutdownNow(); super.handleOnDestroy(); }
}
