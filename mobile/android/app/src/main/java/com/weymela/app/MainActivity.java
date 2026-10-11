package com.weymela.app;

import com.getcapacitor.BridgeActivity;
import android.os.Bundle;

public class MainActivity extends BridgeActivity {
    @Override public void onCreate(Bundle state) {
        registerPlugin(PrivateCookieBridge.class);
        registerPlugin(RestrictedHttpBridge.class);
        registerPlugin(WeymelaApi.class);
        super.onCreate(state);
    }
}
