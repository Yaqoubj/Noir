package app.noir.mobile;
import android.view.*;
import android.widget.ScrollView;

final class PhonePullScroll extends ScrollView {
 final Runnable refresh;final java.util.function.Consumer<Boolean> indicator;float downX,downY;boolean eligible,pulling;
 PhonePullScroll(android.content.Context c,Runnable action,java.util.function.Consumer<Boolean> show){super(c);refresh=action;indicator=show;}
 @Override public boolean onInterceptTouchEvent(MotionEvent e){if(e.getActionMasked()==MotionEvent.ACTION_DOWN){downX=e.getX();downY=e.getY();eligible=getScrollY()==0;pulling=false;}if(e.getActionMasked()==MotionEvent.ACTION_MOVE&&eligible&&getScrollY()==0&&e.getY()-downY>getResources().getDisplayMetrics().density*28&&e.getY()-downY>Math.abs(e.getX()-downX)*1.5f){pulling=true;return true;}return super.onInterceptTouchEvent(e);}
 @Override public boolean onTouchEvent(MotionEvent e){if(e.getActionMasked()==MotionEvent.ACTION_DOWN){downX=e.getX();downY=e.getY();eligible=getScrollY()==0;pulling=false;}if(!pulling&&e.getActionMasked()==MotionEvent.ACTION_MOVE&&eligible&&getScrollY()==0&&e.getY()-downY>getResources().getDisplayMetrics().density*28&&e.getY()-downY>Math.abs(e.getX()-downX)*1.5f){MotionEvent cancel=MotionEvent.obtain(e);cancel.setAction(MotionEvent.ACTION_CANCEL);super.onTouchEvent(cancel);cancel.recycle();pulling=true;}if(!pulling)return super.onTouchEvent(e);float distance=Math.max(0,e.getY()-downY),threshold=80*getResources().getDisplayMetrics().density;if(e.getActionMasked()==MotionEvent.ACTION_MOVE){indicator.accept(distance>=threshold);if(getChildCount()>0)getChildAt(0).setTranslationY(Math.min(distance*.22f,threshold*.45f));}if(e.getActionMasked()==MotionEvent.ACTION_UP||e.getActionMasked()==MotionEvent.ACTION_CANCEL){boolean fire=e.getActionMasked()==MotionEvent.ACTION_UP&&distance>=threshold;pulling=false;if(getChildCount()>0)getChildAt(0).setTranslationY(0);indicator.accept(fire);if(fire){Motion.haptic(this);refresh.run();}performClick();}return true;}
 @Override public boolean performClick(){super.performClick();return true;}
 @Override protected void onDetachedFromWindow(){pulling=false;eligible=false;super.onDetachedFromWindow();}
}
