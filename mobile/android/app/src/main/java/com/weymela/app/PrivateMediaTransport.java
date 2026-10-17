package com.weymela.app;

import java.io.FilterInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;

/** Streams private media without putting cookies or large assets in JavaScript. */
final class PrivateMediaTransport {
    interface ConnectionFactory { HttpURLConnection open(URI uri) throws IOException; }
    static final class Response implements AutoCloseable {
        final int status; final String type; final Map<String,String> headers; final InputStream body;
        Response(int status, String type, Map<String,String> headers, InputStream body) {
            this.status=status;this.type=type;this.headers=Collections.unmodifiableMap(headers);this.body=body;
        }
        @Override public void close() throws IOException { body.close(); }
    }
    static Response open(String localUrl, boolean download, String cookie, String agent, String requestedRange,
                         ConnectionFactory factory) throws IOException {
        URI uri=ProductionMediaPolicy.production(localUrl,download);
        if(uri==null || cookie==null || cookie.isEmpty()) throw new IOException("Private media unavailable.");
        HttpURLConnection connection=factory.open(uri);
        try {
            connection.setInstanceFollowRedirects(false);connection.setRequestMethod("GET");
            connection.setConnectTimeout(30000);connection.setReadTimeout(120000);connection.setUseCaches(false);
            connection.setRequestProperty("Cookie",cookie);connection.setRequestProperty("X-Weymela-Request","1");
            connection.setRequestProperty("Cache-Control","no-store");connection.setRequestProperty("Accept-Encoding","identity");
            if(agent!=null && !agent.contains("\r") && !agent.contains("\n"))connection.setRequestProperty("User-Agent",agent);
            String range=ProductionMediaPolicy.range(requestedRange);
            if(!download && range!=null)connection.setRequestProperty("Range",range);
            int status=connection.getResponseCode();
            if(status!=200 && (download || status!=206))
                throw new IOException("Private media unavailable.");
            String rawType=connection.getContentType();String type=rawType==null?"":rawType.split(";",2)[0].trim().toLowerCase(Locale.ROOT);
            if(!type.equals("video/mp4") && (download || !type.equals("image/jpeg") && !type.equals("image/png")))
                throw new IOException("Unsupported private media.");
            long length=connection.getContentLengthLong();
            if(length>ProductionMediaPolicy.MAX_BYTES || length==0)throw new IOException("Private media size is invalid.");
            Map<String,String> headers=new LinkedHashMap<>();headers.put("Cache-Control","no-store");headers.put("X-Content-Type-Options","nosniff");
            if(length>=0)headers.put("Content-Length",Long.toString(length));
            if(status==206){
                String contentRange=connection.getHeaderField("Content-Range");
                if(contentRange==null || !contentRange.matches("bytes [0-9]{1,10}-[0-9]{1,10}/[0-9]{1,10}"))throw new IOException("Invalid private media range.");
                String[] bounds=contentRange.substring(6).split("[-/]");long start=Long.parseLong(bounds[0]),end=Long.parseLong(bounds[1]),total=Long.parseLong(bounds[2]);
                if(start>end || end>=total || total>ProductionMediaPolicy.MAX_BYTES || length>=0 && length!=end-start+1)throw new IOException("Invalid private media range.");
                headers.put("Content-Range",contentRange);
            }
            if("bytes".equals(connection.getHeaderField("Accept-Ranges")))headers.put("Accept-Ranges","bytes");
            InputStream stream=new FilterInputStream(connection.getInputStream()) {
                long read;boolean closed;
                private int count(int amount)throws IOException{if(amount>0 && (read+=amount)>ProductionMediaPolicy.MAX_BYTES){close();throw new IOException("Private media too large.");}if(amount<0 && length>=0 && read!=length){close();throw new IOException("Incomplete private media.");}return amount;}
                @Override public int read()throws IOException{int value=in.read();count(value>=0?1:-1);return value;}
                @Override public int read(byte[] bytes,int offset,int count)throws IOException{return count(in.read(bytes,offset,count));}
                @Override public void close()throws IOException{if(!closed){closed=true;try{super.close();}finally{connection.disconnect();}}}
            };
            return new Response(status,type,headers,stream);
        } catch(IOException | RuntimeException error){connection.disconnect();throw error;}
    }
    private PrivateMediaTransport() {}
}
