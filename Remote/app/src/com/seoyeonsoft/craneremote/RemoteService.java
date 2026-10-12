package com.seoyeonsoft.craneremote;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.app.usage.UsageEvents;
import android.app.usage.UsageStatsManager;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.graphics.PixelFormat;
import android.hardware.display.DisplayManager;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.view.Display;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;

/** Moonlight 스트리밍 화면이 열려 있는 동안만 Beam Pro 화면(0번)에 리모컨을 덮어 띄운다. 닫히면 내려서 공간 마우스로 돌아간다. */
public class RemoteService extends Service {
    static final String GAME = "com.limelight.Game";
    static final long POLL_MS = 700;
    final Handler h = new Handler(Looper.getMainLooper());
    UsageStatsManager usm;
    WindowManager wm;
    View overlay, bubble;
    boolean gameOn, hiddenByUser;
    long since;

    @Override
    public void onCreate() {
        super.onCreate();
        NotificationManager nm = getSystemService(NotificationManager.class);
        nm.createNotificationChannel(new NotificationChannel("remote", "크레인 리모컨", NotificationManager.IMPORTANCE_MIN));
        Notification n = new Notification.Builder(this, "remote").setContentTitle("크레인 리모컨 대기 중")
                .setContentText("Moonlight가 열리면 리모컨이 나타납니다").setSmallIcon(android.R.drawable.ic_media_play).build();
        startForeground(1, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        usm = getSystemService(UsageStatsManager.class);
        Display phone = getSystemService(DisplayManager.class).getDisplay(Display.DEFAULT_DISPLAY);
        wm = createDisplayContext(phone).getSystemService(WindowManager.class);
        since = System.currentTimeMillis() - 86_400_000L;   // 켜지기 전부터 열려 있던 Moonlight 도 잡도록 하루치 기록으로 현재 상태를 정한다
        Pad.send(this, "PING");
        sendId();
        h.post(this::poll);
        Thread relay = new Thread(this::relayHead, "head-relay");
        relay.setDaemon(true);
        relay.start();
    }

    // 안경 IMU(169.254.2.1:52998)를 같은 포트로 그대로 중계한다 — 서버의 XrealHeadTracker 가 Beam Pro 로 접속해 읽는다.
    void relayHead() {
        try (java.net.ServerSocket ss = new java.net.ServerSocket(52998)) {
            while (true) {
                java.net.Socket in = ss.accept();
                if (!in.getInetAddress().isSiteLocalAddress()) { in.close(); continue; }
                new Thread(() -> {
                    try (java.net.Socket client = in; java.net.Socket glass = new java.net.Socket()) {
                        glass.connect(new java.net.InetSocketAddress("169.254.2.1", 52998), 2000);
                        client.setTcpNoDelay(true);
                        java.io.InputStream src = glass.getInputStream();
                        java.io.OutputStream dst = client.getOutputStream();
                        byte[] buf = new byte[8192];
                        for (int n; (n = src.read(buf)) > 0; ) dst.write(buf, 0, n);
                    } catch (Exception ignored) { }
                }, "head-pipe").start();
            }
        } catch (Exception ignored) { }
    }

    void poll() {
        long now = System.currentTimeMillis();
        UsageEvents ev = usm.queryEvents(since, now);
        UsageEvents.Event e = new UsageEvents.Event();
        while (ev.hasNextEvent()) {
            ev.getNextEvent(e);
            if (!GAME.equals(e.getClassName())) continue;
            int t = e.getEventType();
            if (t == UsageEvents.Event.ACTIVITY_RESUMED) {
                if (!gameOn) { hiddenByUser = false; if (now - e.getTimeStamp() < 5000) { recenter(); sendId(); } }
                gameOn = true;
            }
            else if (t == UsageEvents.Event.ACTIVITY_STOPPED) gameOn = false;
        }
        since = now - 1;
        apply();
        h.postDelayed(this::poll, POLL_MS);
    }

    Boolean pointerShown;

    // Nebula 공간 포인터 — Moonlight 실행 중엔 숨기고 닫히면 되살린다(adb 로 WRITE_SECURE_SETTINGS 를 줘야 동작).
    void pointer(boolean show) {
        if (Boolean.valueOf(show).equals(pointerShown)) return;
        try {
            android.provider.Settings.Global.putInt(getContentResolver(), "xreal_hide_pointicon", show ? 0 : 1);
            pointerShown = show;
        } catch (SecurityException e) { pointerShown = show; }
    }

    // Moonlight 를 막 연 순간엔 안경을 쓰고 앞을 보고 있다 — 그 방향을 서버 머리 추적의 정면으로(R 키).
    // 이 Beam Pro 의 기기 ID — 서버 수신기가 받아 두면 관전 앱이 사용자 등록을 찾는다.
    void sendId() {
        String id = android.provider.Settings.Secure.getString(getContentResolver(), android.provider.Settings.Secure.ANDROID_ID);
        if (id != null) Pad.send(this, "ID BEAM:" + id.toLowerCase());
    }

    void recenter() {
        Pad.send(this, "D CENTER");
        h.postDelayed(() -> Pad.send(this, "U CENTER"), 100);
    }

    // Nebula(안경 공간)가 떠 있어야 Moonlight 가 안경에 따로 뜬다 — 꺼져 있으면 안경이 폰 화면을 비추니 리모컨을 띄우면 안경에 보인다.
    boolean nebulaSpace() {
        try { return "1".equals(android.provider.Settings.Global.getString(getContentResolver(), "xreal.launcher_is_ready")); }
        catch (Exception e) { return true; }
    }

    void apply() {
        boolean onGlasses = gameOn && nebulaSpace();
        pointer(!onGlasses);
        boolean want = onGlasses && !hiddenByUser;
        if (want && overlay == null) show();
        else if (!want && overlay != null) hide();
        boolean wantBubble = onGlasses && hiddenByUser;
        if (wantBubble && bubble == null) showBubble();
        else if (!wantBubble && bubble != null) { wm.removeView(bubble); bubble = null; }
    }

    void show() {
        Context c = createDisplayContext(wm.getDefaultDisplay());
        Button hideBtn = Pad.plain(c, "리모컨 숨기기 (공간 마우스로)");
        hideBtn.setOnClickListener(v -> { hiddenByUser = true; apply(); });
        overlay = Pad.build(c, hideBtn);
        WindowManager.LayoutParams lp = new WindowManager.LayoutParams(-1, -1,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON,
                PixelFormat.OPAQUE);
        wm.addView(overlay, lp);
    }

    // 숨긴 동안 오른쪽 가장자리에 남는 작은 버튼 — 누르면 리모컨으로 돌아온다. 나머지 화면은 공간 마우스가 그대로 받는다.
    void showBubble() {
        Context c = createDisplayContext(wm.getDefaultDisplay());
        Button b = Pad.plain(c, "리모컨");
        b.setBackground(Pad.round(c, android.graphics.Color.rgb(35, 96, 143)));
        b.setOnClickListener(v -> { hiddenByUser = false; apply(); });
        WindowManager.LayoutParams lp = new WindowManager.LayoutParams(Pad.dp(c, 96), Pad.dp(c, 64),
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,
                PixelFormat.TRANSLUCENT);
        lp.gravity = android.view.Gravity.END | android.view.Gravity.CENTER_VERTICAL;
        bubble = b;
        wm.addView(bubble, lp);
    }

    void hide() {
        if (overlay == null) return;
        wm.removeView(overlay);
        overlay = null;
    }

    @Override
    public int onStartCommand(Intent i, int flags, int id) {
        if (i != null && i.hasExtra("stage")) stage(i.getBooleanExtra("stage", false));
        return START_STICKY;
    }

    View stageView;
    WindowManager stageWm;
    android.hardware.display.VirtualDisplay vd;

    // 안경 화면 전체를 덮는 창에 우리 가상 화면을 깔고 Moonlight 를 거기서 띄운다 — Nebula 창(작고 45fps) 대신 패널을 꽉 채운다.
    void stage(boolean on) {
        if (vd != null) { vd.release(); vd = null; }
        if (stageView != null) { stageWm.removeView(stageView); stageView = null; }
        if (!on) return;
        Display[] ds = getSystemService(DisplayManager.class).getDisplays(DisplayManager.DISPLAY_CATEGORY_PRESENTATION);
        if (ds.length == 0) return;
        Display g = ds[0];
        int mode = 0;
        for (Display.Mode m : g.getSupportedModes()) if (m.getPhysicalWidth() == 1920 && m.getPhysicalHeight() == 1080) mode = m.getModeId();
        Context c = createDisplayContext(g).createWindowContext(WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY, null);
        stageWm = c.getSystemService(WindowManager.class);
        android.view.SurfaceView sv = new android.view.SurfaceView(c);
        sv.getHolder().setFixedSize(1920, 1080);
        sv.getHolder().addCallback(new android.view.SurfaceHolder.Callback() {
            public void surfaceCreated(android.view.SurfaceHolder hd) {
                vd = getSystemService(DisplayManager.class).createVirtualDisplay(new android.hardware.display.VirtualDisplayConfig.Builder("crane-stage", 1920, 1080, 320)
                        .setSurface(hd.getSurface()).setRequestedRefreshRate(90f)
                        .setFlags(DisplayManager.VIRTUAL_DISPLAY_FLAG_PUBLIC | DisplayManager.VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY | DisplayManager.VIRTUAL_DISPLAY_FLAG_PRESENTATION).build(), null, null);
                android.util.Log.i("CraneRemote", "stage display " + vd.getDisplay().getDisplayId());
                try {
                    android.app.ActivityOptions o = android.app.ActivityOptions.makeBasic().setLaunchDisplayId(vd.getDisplay().getDisplayId());
                    Intent m = new Intent(Intent.ACTION_MAIN).setClassName("com.limelight", "com.limelight.PcView");
                    m.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_MULTIPLE_TASK);
                    startActivity(m, o.toBundle());
                } catch (Exception e) { android.util.Log.e("CraneRemote", "stage launch", e); }
            }
            public void surfaceChanged(android.view.SurfaceHolder hd, int f, int w, int h2) { }
            public void surfaceDestroyed(android.view.SurfaceHolder hd) { if (vd != null) { vd.release(); vd = null; } }
        });
        WindowManager.LayoutParams lp = new WindowManager.LayoutParams(-1, -1,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.OPAQUE);
        lp.preferredDisplayModeId = mode;
        stageView = sv;
        stageWm.addView(sv, lp);
    }

    @Override
    public void onDestroy() {
        h.removeCallbacksAndMessages(null);
        pointer(true);
        hide();
        if (bubble != null) wm.removeView(bubble);
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent i) { return null; }
}
