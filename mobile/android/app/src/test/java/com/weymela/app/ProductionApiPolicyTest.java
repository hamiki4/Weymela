package com.weymela.app;

import org.junit.Test;
import static org.junit.Assert.*;

public class ProductionApiPolicyTest {
    @Test public void confinesApiToProduction() {
        assertTrue(ProductionApiPolicy.allows("https://api.weymela.com/api/session"));
        assertTrue(ProductionApiPolicy.allows("https://api.weymela.com/api/businesses?search=hello%20world"));
        assertFalse(ProductionApiPolicy.allows("https://api.weymela.com/api/V1/auth/refresh"));
        assertFalse(ProductionApiPolicy.allows("https://api.weymela.com/api/%76%31/auth/refresh"));
        assertFalse(ProductionApiPolicy.allows("https://api.weymela.com/api/%252e%252e/private"));
        for (String url : new String[]{"http://api.weymela.com/api/session", "https://api-pilot.weymela.com/api/session", "https://attacker.invalid/api/session", "https://api.weymela.com:444/api/session", "https://user@api.weymela.com/api/session", "https://api.weymela.com/api/v1/auth/refresh", "https://api.weymela.com/api/../private", "https://api.weymela.com/api/%2e%2e/private", "https://api.weymela.com/api/session#secret"})
            assertFalse(url, ProductionApiPolicy.allows(url));
    }
    @Test public void preservesProtectedHeaders() {
        assertTrue(ProductionApiPolicy.headerAllowed("Idempotency-Key"));
        assertTrue(ProductionApiPolicy.headerAllowed("X-Weymela-Profile"));
        for (String header : new String[]{"Cookie", "Authorization", "Origin", "Host", "Referer"}) assertFalse(header, ProductionApiPolicy.headerAllowed(header));
        assertFalse(ProductionApiPolicy.responseHeaderAllowed("Set-Cookie"));
        assertFalse(ProductionApiPolicy.responseHeaderAllowed("SET-COOKIE"));
        assertTrue(ProductionApiPolicy.responseHeaderAllowed("X-Correlation-ID"));
    }
}
