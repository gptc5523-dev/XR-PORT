package com.seoyeonsoft.craneremote;

import android.content.Context;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.view.Gravity;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.View;
import android.widget.Button;
import android.widget.GridLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** 리모컨 화면(상하좌우·선택)과 UDP 전송. 액티비티·오버레이가 같이 쓴다. */
class Pad {
    static final ExecutorService NET = Executors.newSingleThreadExecutor();
    static DatagramSocket sock;

    static String target(Context c) {
        return c.getSharedPreferences("remote", Context.MODE_PRIVATE).getString("target", "192.168.0.251:5017");
    }

    static void send(Context c, String msg) {
        String t = target(c);
        NET.execute(() -> {
            try {
                int i = t.lastIndexOf(':');
                if (sock == null) sock = new DatagramSocket();
                byte[] d = msg.getBytes(StandardCharsets.UTF_8);
                sock.send(new DatagramPacket(d, d.length, InetAddress.getByName(t.substring(0, i)), Integer.parseInt(t.substring(i + 1))));
            } catch (Exception ignored) { }
        });
    }

    /** 리모컨 화면. extra = 맨 아래에 붙일 버튼(없으면 null). */
    static View build(Context c, View extra) {
        LinearLayout root = new LinearLayout(c);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.rgb(16, 21, 28));
        root.setPadding(dp(c, 20), dp(c, 40), dp(c, 20), dp(c, 24));
        root.setGravity(Gravity.CENTER_HORIZONTAL);

        TextView title = new TextView(c);
        title.setText("크레인 리모컨  ·  " + target(c));
        title.setTextColor(Color.rgb(151, 166, 181));
        title.setTextSize(14);
        root.addView(title);

        root.addView(new View(c), new LinearLayout.LayoutParams(-1, 0, 1));

        GridLayout pad = new GridLayout(c);
        pad.setColumnCount(3);
        pad.setRowCount(3);
        String[][] codes = {{null, "LOOK_UP", null}, {"TURN_L", null, "TURN_R"}, {null, "LOOK_DOWN", null}};
        String[][] labels = {{null, "▲\n위 보기", null}, {"◀\n좌회전", null, "▶\n우회전"}, {null, "▼\n아래 보기", null}};
        android.util.DisplayMetrics dm = c.getResources().getDisplayMetrics();
        int size = (Math.min(dm.widthPixels, dm.heightPixels) - dp(c, 40) - dp(c, 24)) / 3;   // 가로로 돌아가도(폰에서 Moonlight) 짧은 변 기준
        for (int r = 0; r < 3; r++)
            for (int col = 0; col < 3; col++) {
                View v = r == 1 && col == 1 ? new Stick(c) : codes[r][col] == null ? new View(c) : key(c, codes[r][col], labels[r][col], 17);
                GridLayout.LayoutParams lp = new GridLayout.LayoutParams(GridLayout.spec(r), GridLayout.spec(col));
                lp.width = size; lp.height = size;
                lp.setMargins(dp(c, 4), dp(c, 4), dp(c, 4), dp(c, 4));
                pad.addView(v, lp);
            }
        root.addView(pad, new LinearLayout.LayoutParams(-2, -2));
        root.addView(new View(c), new LinearLayout.LayoutParams(-1, 0, 1));

        LinearLayout cranes = new LinearLayout(c);
        cranes.setBaselineAligned(false);   // 기준선 정렬이면 두 줄로 꺾인 버튼만 아래로 처졌다
        String[][] cr = {{"CRANE1", "STS"}, {"CRANE2", "RTG1"}, {"CRANE3", "RTG2"}, {"CENTER", "정면"}};
        for (int i = 0; i < cr.length; i++) {
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, dp(c, 64), 1);
            if (i > 0) lp.leftMargin = dp(c, 10);
            Button b = (Button) key(c, cr[i][0], cr[i][1], 20);
            b.setPadding(0, 0, 0, 0); b.setMinWidth(0); b.setMinimumWidth(0); b.setMaxLines(1);   // 칸이 72dp 안팎 — 기본 패딩·최소폭이면 "RTG1" 이 숫자에서 꺾여 잘렸다
            cranes.addView(b, lp);
        }
        root.addView(cranes, new LinearLayout.LayoutParams(-1, -2));

        LinearLayout.LayoutParams okLp = new LinearLayout.LayoutParams(-1, dp(c, 84));
        okLp.topMargin = dp(c, 16);
        okLp.bottomMargin = dp(c, 12);
        root.addView(key(c, "OK", "선택", 26), okLp);
        if (extra != null) root.addView(extra, new LinearLayout.LayoutParams(-1, dp(c, 56)));
        return root;
    }

    static View key(Context c, String code, String label, int sp) {
        Button k = new Button(c);
        k.setText(label);
        k.setAllCaps(false);
        k.setTextSize(sp);
        k.setTextColor(Color.WHITE);
        k.setBackground(round(c, code.equals("OK") ? Color.rgb(35, 96, 143) : Color.rgb(38, 49, 60)));
        k.setOnTouchListener((v, e) -> {
            int a = e.getActionMasked();
            if (a == MotionEvent.ACTION_DOWN) {
                v.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
                v.setAlpha(0.6f);
                send(c, "D " + code);
            } else if (a == MotionEvent.ACTION_UP || a == MotionEvent.ACTION_CANCEL) {
                v.setAlpha(1f);
                send(c, "U " + code);
            }
            return true;
        });
        return k;
    }

    static Button plain(Context c, String s) {
        Button b = new Button(c);
        b.setText(s);
        b.setTextColor(Color.WHITE);
        b.setBackground(round(c, Color.rgb(52, 66, 79)));
        return b;
    }

    static GradientDrawable round(Context c, int color) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(dp(c, 18));
        return g;
    }

    static int dp(Context c, int v) { return Math.round(v * c.getResources().getDisplayMetrics().density); }

    /** 가운데 조이스틱 — 끄는 방향 한 곳으로 W·A·S·D 하나를 누른 채 유지한다. */
    static class Stick extends View {
        static final float DEAD = 0.35f;
        final android.graphics.Paint base = new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG);
        final android.graphics.Paint knob = new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG);
        float kx, ky;
        final java.util.Set<String> held = new java.util.HashSet<>();

        Stick(Context c) {
            super(c);
            base.setColor(Color.rgb(38, 49, 60));
            knob.setColor(Color.rgb(35, 96, 143));
        }

        @Override
        protected void onDraw(android.graphics.Canvas g) {
            float cx = getWidth() / 2f, cy = getHeight() / 2f, r = Math.min(cx, cy);
            g.drawCircle(cx, cy, r, base);
            g.drawCircle(cx + kx * r * 0.55f, cy + ky * r * 0.55f, r * 0.42f, knob);
        }

        @Override
        public boolean onTouchEvent(MotionEvent e) {
            float cx = getWidth() / 2f, cy = getHeight() / 2f, r = Math.min(cx, cy);
            int a = e.getActionMasked();
            if (a == MotionEvent.ACTION_UP || a == MotionEvent.ACTION_CANCEL) { kx = 0; ky = 0; }
            else {
                if (a == MotionEvent.ACTION_DOWN) performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
                kx = (e.getX() - cx) / r; ky = (e.getY() - cy) / r;
                float m = (float) Math.hypot(kx, ky);
                if (m > 1) { kx /= m; ky /= m; }
            }
            java.util.Set<String> want = new java.util.HashSet<>();
            String dir = dir(kx, ky);
            if (dir != null) want.add(dir);
            for (String k : new java.util.ArrayList<>(held)) if (!want.contains(k)) { send(getContext(), "U " + k); held.remove(k); }
            for (String k : want) if (held.add(k)) send(getContext(), "D " + k);
            invalidate();
            return true;
        }

        // 한 번에 한 방향만 — 더 많이 민 축을 고른다(위=앞, 아래=뒤, 좌우=옆걸음).
        static String dir(float kx, float ky) {
            if (Math.hypot(kx, ky) < DEAD) return null;
            if (Math.abs(ky) >= Math.abs(kx)) return ky < 0 ? "FWD" : "BACK";
            return kx < 0 ? "STRAFE_L" : "STRAFE_R";
        }
    }
}
