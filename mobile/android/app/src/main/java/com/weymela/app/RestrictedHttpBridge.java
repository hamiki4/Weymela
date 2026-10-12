package com.weymela.app;

import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

/** Block the general HTTP bridge; the Production API bridge enforces host/cookie rules. */
@CapacitorPlugin(name = "CapacitorHttp")
public class RestrictedHttpBridge extends Plugin {
    @PluginMethod public void request(PluginCall call) { call.reject("Use the Weymela API client."); }
    @PluginMethod public void get(PluginCall call) { request(call); }
    @PluginMethod public void post(PluginCall call) { request(call); }
    @PluginMethod public void put(PluginCall call) { request(call); }
    @PluginMethod public void patch(PluginCall call) { request(call); }
    @PluginMethod public void delete(PluginCall call) { request(call); }
}
