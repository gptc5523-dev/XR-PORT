#!/bin/bash
# Unity 에 딸린 Android SDK·JDK 로 Gradle 없이 APK 를 만든다(폐쇄망에서도 빌드됨). 결과: Remote/build/CraneRemote.apk
set -euo pipefail
cd "$(dirname "$0")"
AP="${UNITY_ANDROID:-/Applications/Unity/Hub/Editor/6000.4.5f1/PlaybackEngines/AndroidPlayer}"
BT="$AP/SDK/build-tools/36.0.0"; JAR="$AP/SDK/platforms/android-34/android.jar"; JB="$AP/OpenJDK/bin"
KS="${KEYSTORE:-$HOME/.android/crane-remote.keystore}"   # 서명 키가 바뀌면 Beam Pro 기기 ID(ANDROID_ID)도 바뀐다 — 이 키를 보관
export CRANE_KS_PASS="${CRANE_KS_PASS:-$(security find-generic-password -s crane-remote-keystore -w)}"
rm -rf build && mkdir -p build/classes build/dex
"$BT/aapt2" link -o build/base.apk -I "$JAR" --manifest app/AndroidManifest.xml
"$JB/javac" --release 17 -cp "$JAR" -d build/classes $(find app/src -name '*.java')
"$BT/d8" --min-api 29 --lib "$JAR" --output build/dex $(find build/classes -name '*.class')
cp build/base.apk build/unsigned.apk && (cd build/dex && zip -q ../unsigned.apk classes.dex)
"$BT/zipalign" -f 4 build/unsigned.apk build/aligned.apk
JAVA_HOME="$AP/OpenJDK" "$BT/apksigner" sign --ks "$KS" --ks-pass env:CRANE_KS_PASS --key-pass env:CRANE_KS_PASS --out build/CraneRemote.apk build/aligned.apk
ls -l build/CraneRemote.apk
