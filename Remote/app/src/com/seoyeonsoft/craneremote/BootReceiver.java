package com.seoyeonsoft.craneremote;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** 부팅·앱 업데이트 뒤 리모컨 서비스를 다시 켠다 — 설정 화면을 열지 않아도 Moonlight 감시가 이어진다. */
public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context c, Intent i) {
        String t = i.getStringExtra("target");
        if (t != null) c.getSharedPreferences("remote", Context.MODE_PRIVATE).edit().putString("target", t).commit();
        Intent s = new Intent(c, RemoteService.class);
        if (i.hasExtra("stage")) s.putExtra("stage", i.getBooleanExtra("stage", false));
        else c.stopService(s);
        c.startForegroundService(s);
    }
}
