package app.noir.mobile;
import android.animation.*;
import android.graphics.*;
import android.graphics.drawable.*;
import android.content.res.ColorStateList;
import android.provider.Settings;
import android.view.*;
import android.widget.*;
import java.util.*;

final class Motion {
 private static final int TAG=0x7e001031;
 private static final android.util.LruCache<String,Integer> COLORS=new android.util.LruCache<>(192);
 private static final class Life implements View.OnAttachStateChangeListener {final List<Animator> running=new ArrayList<>();final List<Runnable> callbacks=new ArrayList<>();Runnable pending;public void onViewAttachedToWindow(View v){Runnable r=pending;pending=null;if(r!=null)r.run();}public void onViewDetachedFromWindow(View v){pending=null;for(Runnable r:callbacks)v.removeCallbacks(r);callbacks.clear();v.animate().cancel();for(Animator a:new ArrayList<>(running))a.cancel();running.clear();v.setAlpha(1);v.setTranslationX(0);v.setTranslationY(0);v.setScaleX(1);v.setScaleY(1);}}
 static boolean enabled(View v){try{return Settings.Global.getFloat(v.getContext().getContentResolver(),Settings.Global.ANIMATOR_DURATION_SCALE,1)>0;}catch(Exception e){return true;}}
 private static Life life(View v){Life l=(Life)v.getTag(TAG);if(l==null){l=new Life();v.setTag(TAG,l);v.addOnAttachStateChangeListener(l);}return l;}
 private static void ready(View v,Runnable r){Life l=life(v);if(v.isAttachedToWindow())r.run();else l.pending=r;}
 private static void run(View v,ValueAnimator animator){Life l=life(v);l.running.add(animator);animator.addListener(new AnimatorListenerAdapter(){@Override public void onAnimationEnd(Animator a){l.running.remove(a);}});animator.start();}
 static void after(View v,long delay,Runnable action){Life l=life(v);Runnable r=new Runnable(){public void run(){l.callbacks.remove(this);if(v.isAttachedToWindow())action.run();}};l.callbacks.add(r);v.postDelayed(r,enabled(v)?delay:0);}
 static void fadeIn(View v){if(!enabled(v))return;ready(v,()->{v.animate().cancel();v.setAlpha(0);v.animate().alpha(1).setStartDelay(0).setDuration(260).start();});}
 static void riseIn(View v,int stagger){if(!enabled(v))return;ready(v,()->{v.setAlpha(0);v.setTranslationY(14*v.getResources().getDisplayMetrics().density);v.animate().alpha(1).translationY(0).setStartDelay(Math.min(stagger,6)*38L).setDuration(300).start();});}
 static void sections(android.view.ViewGroup host){for(int n=0;n<Math.min(host.getChildCount(),10);n++)riseIn(host.getChildAt(n),n);}
 static void pressScale(View v){life(v);v.setOnTouchListener((view,e)->{if(enabled(view)){if(e.getActionMasked()==MotionEvent.ACTION_DOWN)view.animate().scaleX(.96f).scaleY(.96f).setStartDelay(0).setDuration(85).start();else if(e.getActionMasked()==MotionEvent.ACTION_UP||e.getActionMasked()==MotionEvent.ACTION_CANCEL)view.animate().scaleX(1).scaleY(1).setStartDelay(0).setDuration(140).start();}return false;});}
 static void card(View v){pressScale(v);v.setForeground(new RippleDrawable(ColorStateList.valueOf(0x35e4c084),null,MainActivity.bg(Color.WHITE,14*v.getResources().getDisplayMetrics().density)));}
 static void navSelect(View v,int offset){if(!enabled(v))return;ready(v,()->{v.setTranslationX(offset);v.setScaleX(.92f);v.setScaleY(.92f);v.animate().translationX(0).scaleX(1).scaleY(1).setStartDelay(0).setDuration(240).start();});}
 static void pop(View v){if(!enabled(v))return;ready(v,()->{v.setScaleX(.9f);v.setScaleY(.9f);v.animate().scaleX(1).scaleY(1).setStartDelay(0).setDuration(220).setInterpolator(new android.view.animation.OvershootInterpolator(.8f)).start();});}
 static void countUp(TextView v,int target){if(!enabled(v)){v.setText(String.valueOf(target));return;}ready(v,()->{ValueAnimator a=ValueAnimator.ofInt(0,target);a.setDuration(480);a.addUpdateListener(x->v.setText(String.valueOf(x.getAnimatedValue())));run(v,a);});}
 static void checkMorph(TextView v){v.setText("✓");v.setTextColor(MainActivity.BLACK);GradientDrawable b=MainActivity.bg(MainActivity.GOLD,100);v.setBackground(b);pop(v);haptic(v);}
 static void haptic(View v){boolean done=false;if(android.os.Build.VERSION.SDK_INT>=30)done=v.performHapticFeedback(HapticFeedbackConstants.CONFIRM);if(!done)v.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP);}
 static void drift(View v){if(!enabled(v))return;ready(v,()->{ValueAnimator a=ValueAnimator.ofFloat(1.02f,1.09f);a.setDuration(18000);a.setRepeatCount(1);a.setRepeatMode(ValueAnimator.REVERSE);a.addUpdateListener(x->{float s=(float)x.getAnimatedValue();v.setScaleX(s);v.setScaleY(s);});run(v,a);});}
 static int placeholder(String key){Integer c=COLORS.get(key);return c==null?Color.HSVToColor(new float[]{Math.floorMod(key.hashCode(),360),.23f,.19f}):c;}
 static void remember(String key,Bitmap b){if(b==null)return;int[] count=new int[512];long[] r=new long[512],g=new long[512],blue=new long[512];int best=0;for(int y=0;y<10;y++)for(int x=0;x<10;x++){int c=b.getPixel(Math.min(b.getWidth()-1,x*b.getWidth()/10),Math.min(b.getHeight()-1,y*b.getHeight()/10)),red=Color.red(c),green=Color.green(c),bl=Color.blue(c),bucket=(red>>5)*64+(green>>5)*8+(bl>>5);count[bucket]++;r[bucket]+=red;g[bucket]+=green;blue[bucket]+=bl;if(count[bucket]>count[best])best=bucket;}if(count[best]>0)COLORS.put(key,Color.rgb((int)(r[best]/count[best]),(int)(g[best]/count[best]),(int)(blue[best]/count[best])));}
 static void loaded(ImageView v,Bitmap b,String key){if(b==null)return;v.setBackgroundColor(placeholder(key));v.setImageBitmap(b);fadeIn(v);}
}
