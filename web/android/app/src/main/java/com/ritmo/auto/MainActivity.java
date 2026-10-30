package com.ritmo.auto;

import android.os.Bundle;
import android.webkit.WebView;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        registerPlugin(RitmoPlaybackPlugin.class);
        super.onCreate(savedInstanceState);
        WebView webView = getBridge() == null ? null : getBridge().getWebView();
        if (webView != null) {
            webView.getSettings().setSupportZoom(false);
            webView.getSettings().setBuiltInZoomControls(false);
            webView.getSettings().setDisplayZoomControls(false);
        }
    }

    @Override
    @SuppressWarnings("deprecation")
    public void onBackPressed() {
        if (getBridge() != null && getBridge().getWebView() != null) {
            getBridge().getWebView().evaluateJavascript(
                "window.dispatchEvent(new Event('ritmo:back'))", null
            );
            return;
        }
        super.onBackPressed();
    }
}
