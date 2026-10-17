package com.weymela.app;

import org.junit.Test;
import static org.junit.Assert.*;

public class ProductionMediaPolicyTest {
    private static final String ID="12345678-abcd-4321-9876-123456789abc";
    @Test public void mapsOnlyExactLocalPrivateMedia(){
        for(String kind:new String[]{"ugc","promotions"})assertEquals("https://api.weymela.com/api/review-media/"+kind+"/"+ID,
            ProductionMediaPolicy.production("https://localhost/api/review-media/"+kind+"/"+ID,false).toString());
        assertNotNull(ProductionMediaPolicy.production("https://localhost/api/review-media/ugc/"+ID+"/download",true));
        assertNotNull(ProductionMediaPolicy.production("https://localhost/api/review-media/ugc/"+ID,true));
        for(String url:new String[]{"http://localhost/api/review-media/ugc/"+ID,"https://localhost:443/api/review-media/ugc/"+ID,
            "https://user@localhost/api/review-media/ugc/"+ID,"https://api.weymela.com/api/review-media/ugc/"+ID,
            "https://localhost.evil.invalid/api/review-media/ugc/"+ID,"https://localhost/api/session",
            "https://localhost/api/review-media/ugc/"+ID+"?token=x","https://localhost/api/review-media/ugc/"+ID+"#x",
            "https://localhost/api/review-media/ugc/%31"+ID.substring(1),"https://localhost/api/review-media/ugc/../ugc/"+ID,
            "https://localhost/api/review-media/ugc/not-a-guid","//localhost/api/review-media/ugc/"+ID})
            assertNull(url,ProductionMediaPolicy.production(url,false));
        assertNull(ProductionMediaPolicy.production("https://localhost/api/review-media/ugc/"+ID+"/download",false));
        assertNull(ProductionMediaPolicy.production("https://localhost/api/review-media/promotions/"+ID+"/download",true));
    }
    @Test public void onlySingleBoundedRanges(){
        for(String range:new String[]{"bytes=0-","bytes=5-9","bytes=-100"})assertEquals(range,ProductionMediaPolicy.range(range));
        for(String range:new String[]{"bytes=0-1,5-6","bytes=5-4","bytes=-0","bytes=9999999999-","bytes=0-9999999999","bytes=0-1\r\nCookie:x","items=1-2","bytes=one-two"})assertNull(range,ProductionMediaPolicy.range(range));
    }
}
