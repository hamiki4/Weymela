package com.weymela.app;

import java.net.URI;
import java.util.regex.Pattern;

/** Only authenticated private review assets from the bundled local application. */
final class ProductionMediaPolicy {
    static final long MAX_BYTES = 100L * 1024 * 1024;
    private static final String ID = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";
    private static final Pattern PREVIEW = Pattern.compile("/api/review-media/(?:promotions|ugc)/" + ID);
    private static final Pattern DOWNLOAD = Pattern.compile("/api/review-media/ugc/" + ID + "(?:/download)?");
    static URI production(String localUrl, boolean download) {
        try {
            URI uri = new URI(localUrl);
            if (!"https".equals(uri.getScheme()) || !"localhost".equals(uri.getHost())
                    || uri.getPort() != -1 || uri.getRawUserInfo() != null || uri.getRawQuery() != null
                    || uri.getRawFragment() != null || uri.getRawPath().contains("%")
                    || !(download ? DOWNLOAD : PREVIEW).matcher(uri.getRawPath()).matches()) return null;
            return new URI("https://api.weymela.com" + uri.getRawPath());
        } catch (Exception ignored) { return null; }
    }
    static String range(String value) {
        if (value == null || value.isEmpty()) return null;
        if (!value.matches("bytes=(?:[0-9]{1,10}-[0-9]{0,10}|-[0-9]{1,10})")) return null;
        String[] parts = value.substring(6).split("-", -1);
        try {
            long start = parts[0].isEmpty() ? -1 : Long.parseLong(parts[0]);
            long end = parts[1].isEmpty() ? -1 : Long.parseLong(parts[1]);
            if (start >= MAX_BYTES || end > MAX_BYTES || start < 0 && end <= 0
                    || start >= 0 && end >= 0 && end < start) return null;
            return value;
        } catch (NumberFormatException ignored) { return null; }
    }
    private ProductionMediaPolicy() {}
}
