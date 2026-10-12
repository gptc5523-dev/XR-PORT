package com.seoyeonsoft.craneremote;

import android.app.Activity;
import android.app.AppOpsManager;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
import android.os.Process;
import android.provider.Settings;
import android.view.inputmethod.EditorInfo;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;

/** 설정 화면 — 서버 주소, 권한 2개, 리모컨 서비스 시작. 리모컨 자체는 RemoteService 가 Moonlight 실행 중에만 띄운다. */
public class MainActivity extends Activity {
    TextView status;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        String t = getIntent().getStringExtra("target");
        if (t != null) getSharedPreferences("remote", MODE_PRIVATE).edit().putString("target", t).commit();
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.rgb(16, 21, 28));
        int p = Pad.dp(this, 20);
        root.setPadding(p, Pad.dp(this, 32), p, p);

        root.addView(text("크레인 리모컨", 22, Color.WHITE));
        root.addView(text("Moonlight 화면이 열리면 이 화면 대신 리모컨이 나타나고, 닫으면 공간 마우스로 돌아갑니다.", 14, Color.rgb(151, 166, 181)));

        EditText target = new EditText(this);
        target.setText(Pad.target(this));
        target.setTextColor(Color.WHITE);
        target.setHint("서버 IP:포트");
        target.setSingleLine(true);
        target.setImeOptions(EditorInfo.IME_ACTION_DONE);
        root.addView(target);
        Button save = Pad.plain(this, "서버 주소 저장");
        save.setOnClickListener(v -> {
            getSharedPreferences("remote", MODE_PRIVATE).edit().putString("target", target.getText().toString().trim()).apply();
            Pad.send(this, "PING");
            refresh();
        });
        root.addView(save, row());

        Button overlay = Pad.plain(this, "권한 1: 다른 앱 위에 표시");
        overlay.setOnClickListener(v -> startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:" + getPackageName()))));
        root.addView(overlay, row());
        Button usage = Pad.plain(this, "권한 2: 사용 정보 접근");
        usage.setOnClickListener(v -> startActivity(new Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS)));
        root.addView(usage, row());

        Button test = Pad.plain(this, "리모컨 미리보기 (버튼 시험)");
        test.setOnClickListener(v -> setContentView(Pad.build(this, null)));
        root.addView(test, row());

        status = text("", 15, Color.WHITE);
        root.addView(status);
        setContentView(root);
    }

    @Override
    protected void onNewIntent(Intent i) {
        super.onNewIntent(i);
        String t = i.getStringExtra("target");
        if (t != null) getSharedPreferences("remote", MODE_PRIVATE).edit().putString("target", t).commit();
        refresh();
    }

    @Override
    protected void onResume() {
        super.onResume();
        refresh();
    }

    void refresh() {
        boolean ov = Settings.canDrawOverlays(this);
        boolean us = getSystemService(AppOpsManager.class).unsafeCheckOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS,
                Process.myUid(), getPackageName()) == AppOpsManager.MODE_ALLOWED;
        if (ov && us) startForegroundService(new Intent(this, RemoteService.class));
        status.setText("다른 앱 위에 표시: " + (ov ? "허용" : "필요") + "\n사용 정보 접근: " + (us ? "허용" : "필요")
                + "\n리모컨 서비스: " + (ov && us ? "실행 중 — Moonlight를 여세요" : "권한 허용 후 자동 시작")
                + "\n서버: " + Pad.target(this));
    }

    LinearLayout.LayoutParams row() {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, Pad.dp(this, 52));
        lp.topMargin = Pad.dp(this, 10);
        return lp;
    }

    TextView text(String s, int sp, int color) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(sp);
        t.setTextColor(color);
        t.setPadding(0, 0, 0, Pad.dp(this, 10));
        return t;
    }
}
