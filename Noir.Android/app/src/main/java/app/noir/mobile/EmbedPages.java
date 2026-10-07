package app.noir.mobile;
import java.net.URI;
import java.util.regex.*;
import org.json.JSONObject;
final class RedditDiscussion {
    static String reddit(String url) {
        try {URI u=new URI(url);String h=u.getHost();
            if(!"https".equals(u.getScheme())||u.getUserInfo()!=null||u.getPort()!=-1||!("reddit.com".equals(h)||"www.reddit.com".equals(h)||"old.reddit.com".equals(h)||"new.reddit.com".equals(h)))return null;
            if(!u.getRawPath().matches("^/r/[A-Za-z0-9_]+/comments/[a-z0-9]+(?:/[A-Za-z0-9_-]+(?:/[a-z0-9]+)?)?/?$"))return null;
            return "https://www.reddit.com"+u.getRawPath().replaceAll("/+$","")+"/";
        }catch(Exception e){return null;}
    }
}
