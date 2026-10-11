package com.weymela.app;

import java.net.URI;
import java.util.Locale;
import java.util.Set;

final class ProductionApiPolicy {
    static final String HOST = "api.weymela.com";
    private static final Set<String> HEADERS = Set.of("accept", "content-type", "x-weymela-request", "x-weymela-profile", "x-weymela-activity", "idempotency-key", "x-correlation-id");
    static boolean allows(String value) {
        try {
            URI uri = new URI(value);
            String path = uri.getRawPath();
            return "https".equals(uri.getScheme()) && HOST.equals(uri.getHost())
                && (uri.getPort() == -1 || uri.getPort() == 443) && uri.getUserInfo() == null && uri.getFragment() == null
                && path != null && path.startsWith("/api/") && !path.startsWith("/api/v1/")
                && !path.contains("\\") && !path.toLowerCase(Locale.ROOT).matches(".*%(2e|2f|5c).*" )
                && path.equals(uri.normalize().getRawPath());
        } catch (Exception ignored) { return false; }
    }
    static boolean headerAllowed(String name) { return HEADERS.contains(name.toLowerCase(Locale.ROOT)); }
    static boolean responseHeaderAllowed(String name) {
        return !Set.of("set-cookie", "set-cookie2", "cookie", "authorization").contains(name.toLowerCase(Locale.ROOT));
    }
}
