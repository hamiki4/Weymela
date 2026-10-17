package com.weymela.app;

import org.junit.Test;
import static org.junit.Assert.*;
import java.io.*;
import java.net.*;
import java.util.*;

public class PrivateMediaTransportTest {
    private static final String URL="https://localhost/api/review-media/ugc/12345678-abcd-4321-9876-123456789abc";
    private static class Fake extends HttpURLConnection{
        int status=200;String type="video/mp4";long length=4;boolean disconnected,opened;InputStream stream=new ByteArrayInputStream(new byte[]{1,2,3,4});Map<String,String> responseHeaders=new HashMap<>();
        Fake()throws Exception{super(new URL("https://api.weymela.com"));}
        @Override public void disconnect(){disconnected=true;}
        @Override public boolean usingProxy(){return false;}
        @Override public void connect(){}
        @Override public int getResponseCode(){return status;}
        @Override public String getContentType(){return type;}
        @Override public long getContentLengthLong(){return length;}
        @Override public String getHeaderField(String name){return responseHeaders.get(name);}
        @Override public InputStream getInputStream(){opened=true;return stream;}
    }
    private static PrivateMediaTransport.Response open(Fake fake,boolean download,String range)throws Exception{
        return PrivateMediaTransport.open(URL+(download?"/download":""),download,"private-session","Weymela test agent",range,uri->{assertEquals("api.weymela.com",uri.getHost());return fake;});
    }
    @Test public void streamsAuthenticatedBytesAndKeepsCookiesNative()throws Exception{
        Fake fake=new Fake();fake.responseHeaders.put("Set-Cookie","private-response-cookie");
        try(PrivateMediaTransport.Response result=open(fake,false,null)){
            assertArrayEquals(new byte[]{1,2,3,4},result.body.readAllBytes());assertEquals("video/mp4",result.type);assertEquals("no-store",result.headers.get("Cache-Control"));
            assertFalse(result.headers.containsKey("Set-Cookie"));assertFalse(result.headers.containsKey("Cookie"));assertEquals("private-session",fake.getRequestProperty("Cookie"));
            assertEquals("1",fake.getRequestProperty("X-Weymela-Request"));assertEquals("Weymela test agent",fake.getRequestProperty("User-Agent"));assertFalse(fake.getInstanceFollowRedirects());assertFalse(fake.getUseCaches());
        }
        assertTrue(fake.disconnected);
    }
    @Test public void supportsSeekRangeAndRejectsIncorrectPartialResponse()throws Exception{
        Fake fake=new Fake();fake.status=206;fake.responseHeaders.put("Content-Range","bytes 4-7/10");fake.responseHeaders.put("Accept-Ranges","bytes");
        try(PrivateMediaTransport.Response result=open(fake,false,"bytes=4-7")){
            assertEquals("bytes=4-7",fake.getRequestProperty("Range"));assertEquals("bytes 4-7/10",result.headers.get("Content-Range"));assertEquals(206,result.status);
        }
        Fake invalid=new Fake();invalid.status=206;invalid.responseHeaders.put("Content-Range","bytes 4-12/10");assertThrows(IOException.class,()->open(invalid,false,"bytes=4-7"));assertTrue(invalid.disconnected);
    }
    @Test public void refusesRedirectsUnauthorizedContentAndPartialDownloads()throws Exception{
        for(int status:new int[]{301,302,307,401,403,404,500}){Fake fake=new Fake();fake.status=status;assertThrows(IOException.class,()->open(fake,false,null));assertTrue(fake.disconnected);assertFalse(fake.opened);}
        for(String type:new String[]{"text/html","application/json","application/octet-stream"}){Fake fake=new Fake();fake.type=type;assertThrows(IOException.class,()->open(fake,false,null));assertFalse(fake.opened);}
        Fake partial=new Fake();partial.status=206;assertThrows(IOException.class,()->open(partial,true,null));
        Fake image=new Fake();image.type="image/png";assertThrows(IOException.class,()->open(image,true,null));
    }
    @Test public void enforcesSizeAndClosesConnectionOnOversizedStream()throws Exception{
        Fake declared=new Fake();declared.length=ProductionMediaPolicy.MAX_BYTES+1;assertThrows(IOException.class,()->open(declared,false,null));assertFalse(declared.opened);
        Fake unknown=new Fake();unknown.length=-1;unknown.stream=new InputStream(){long remaining=ProductionMediaPolicy.MAX_BYTES+1;
            @Override public int read(){return remaining-->0?0:-1;}
            @Override public int read(byte[] b,int off,int len){if(remaining<=0)return -1;int n=(int)Math.min(remaining,len);remaining-=n;return n;}};
        try(PrivateMediaTransport.Response result=open(unknown,false,null)){byte[] buffer=new byte[32768];assertThrows(IOException.class,()->{while(result.body.read(buffer)!=-1){}});}
        assertTrue(unknown.disconnected);
    }
    @Test public void rejectsTruncatedDownloads()throws Exception{
        Fake truncated=new Fake();truncated.length=10;
        try(PrivateMediaTransport.Response result=open(truncated,true,null)){assertThrows(IOException.class,()->result.body.readAllBytes());}
        assertTrue(truncated.disconnected);
    }
    @Test public void rejectsOtherOriginsAndMissingAuthenticationBeforeOpeningConnection()throws Exception{
        PrivateMediaTransport.ConnectionFactory forbidden=uri->{throw new AssertionError("No connection should open");};
        assertThrows(IOException.class,()->PrivateMediaTransport.open("https://attacker.invalid/api/review-media/ugc/x",false,"cookie","agent",null,forbidden));
        assertThrows(IOException.class,()->PrivateMediaTransport.open(URL,false,null,"agent",null,forbidden));
    }
}
