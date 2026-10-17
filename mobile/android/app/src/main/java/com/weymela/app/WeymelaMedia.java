package com.weymela.app;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.provider.DocumentsContract;
import android.webkit.CookieManager;
import androidx.activity.result.ActivityResult;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.ActivityCallback;
import com.getcapacitor.annotation.CapacitorPlugin;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

@CapacitorPlugin(name="WeymelaMedia")
public class WeymelaMedia extends Plugin {
    private final AtomicBoolean saving=new AtomicBoolean();
    private final ExecutorService executor=Executors.newSingleThreadExecutor();
    private String userAgent;
    @Override public void load(){userAgent=getBridge().getWebView().getSettings().getUserAgentString();}
    @PluginMethod public void save(PluginCall call){
        String url=call.getString("url","");
        if(ProductionMediaPolicy.production(url,true)==null){call.reject("Unsupported download.");return;}
        if(!saving.compareAndSet(false,true)){call.reject("A download is already in progress.");return;}
        Intent intent=new Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("video/mp4")
            .putExtra(Intent.EXTRA_TITLE,"weymela-approved-video.mp4");
        getActivity().runOnUiThread(()->{
            try{startActivityForResult(call,intent,"destinationChosen");}
            catch(Exception ignored){saving.set(false);call.reject("Video could not be saved.");}
        });
    }
    @ActivityCallback private void destinationChosen(PluginCall call,ActivityResult result){
        if(call==null){saving.set(false);return;}
        if(result.getResultCode()!=Activity.RESULT_OK || result.getData()==null || result.getData().getData()==null){
            saving.set(false);JSObject answer=new JSObject();answer.put("saved",false);call.resolve(answer);return;
        }
        Uri destination=result.getData().getData();
        if(!"content".equals(destination.getScheme())){saving.set(false);call.reject("Choose a file destination.");return;}
        String url=call.getString("url","");
        executor.execute(()->{
            boolean completed=false;
            try{
                java.net.URI media=ProductionMediaPolicy.production(url,true);
                if(media==null)throw new java.io.IOException("Unsupported download.");
                String cookie=CookieManager.getInstance().getCookie(media.toString());
                try(PrivateMediaTransport.Response response=PrivateMediaTransport.open(url,true,cookie,userAgent,null,
                        uri->(HttpURLConnection)uri.toURL().openConnection());
                    OutputStream output=getContext().getContentResolver().openOutputStream(destination,"w")){
                    if(output==null)throw new java.io.IOException("Unavailable destination.");
                    byte[] buffer=new byte[32768];int count;long total=0;
                    while((count=response.body.read(buffer))!=-1){if(Thread.currentThread().isInterrupted())throw new java.io.IOException("Cancelled.");output.write(buffer,0,count);total+=count;}
                    if(total==0)throw new java.io.IOException("Empty video.");
                    output.flush();
                }
                completed=true;JSObject answer=new JSObject();answer.put("saved",true);call.resolve(answer);
            }catch(Exception ignored){call.reject("Video could not be saved.");}
            finally{
                // ACTION_CREATE_DOCUMENT creates a new user-selected document.
                // Remove only that incomplete output after a transfer failure.
                if(!completed)try{DocumentsContract.deleteDocument(getContext().getContentResolver(),destination);}catch(Exception ignored){}
                saving.set(false);
            }
        });
    }
    @Override protected void handleOnDestroy(){executor.shutdownNow();super.handleOnDestroy();}
}
