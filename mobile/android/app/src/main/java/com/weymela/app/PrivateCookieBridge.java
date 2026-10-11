package com.weymela.app;

import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

/** Session and device cookies belong to native HTTPS transport, never JavaScript. */
@CapacitorPlugin(name = "CapacitorCookies")
public class PrivateCookieBridge extends Plugin {
    @PluginMethod public void getCookies(PluginCall call) { call.reject("Cookie access is unavailable."); }
    @PluginMethod public void setCookie(PluginCall call) { call.reject("Cookie access is unavailable."); }
    @PluginMethod public void deleteCookie(PluginCall call) { call.reject("Cookie access is unavailable."); }
    @PluginMethod public void clearCookies(PluginCall call) { call.reject("Cookie access is unavailable."); }
    @PluginMethod public void clearAllCookies(PluginCall call) { call.reject("Cookie access is unavailable."); }
}
