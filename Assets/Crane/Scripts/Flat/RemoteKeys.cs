using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace AIXRCrane.Crane.Flat
{
    /// <summary>리모컨 직접 입력 — 관전 자리 k 는 127.0.0.1:(6000+k) 로 "D 키"/"U 키" 를 받아 키보드 상태로 넣는다. 서버 화면 하나에 관전 앱이 여럿이라 X 키보드 포커스로는 자리를 나눌 수 없다.</summary>
    public sealed class RemoteKeys : MonoBehaviour
    {
        public const int BasePort = 6000;

        static readonly Dictionary<string, Key> Map = new Dictionary<string, Key>
        {
            { "FWD", Key.W }, { "BACK", Key.S }, { "STRAFE_L", Key.A }, { "STRAFE_R", Key.D },
            { "TURN_L", Key.LeftArrow }, { "TURN_R", Key.RightArrow }, { "LOOK_UP", Key.UpArrow }, { "LOOK_DOWN", Key.DownArrow },
            { "OK", Key.Enter }, { "CRANE1", Key.Digit1 }, { "CRANE2", Key.Digit2 }, { "CRANE3", Key.Digit3 }, { "CENTER", Key.R },
        };

        readonly HashSet<Key> held = new HashSet<Key>();
        readonly object gate = new object();
        UdpClient udp;
        Thread thread;
        volatile bool running, dirty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (Port() > 0) AIXRCrane.Crane.Sts.CraneHud.EnsureSpawned<RemoteKeys>("RemoteKeys");
        }

        /// <summary>이 인스턴스가 받을 포트 — 모바일 관전(-mobile)일 때만, -seat k 면 6000+k(없으면 1번). 아니면 0.</summary>
        public static int Port()
        {
            var a = System.Environment.GetCommandLineArgs();
            bool mobile = false; int seat = 1;
            for (int i = 0; i < a.Length; i++)
            {
                if (string.Equals(a[i], "-mobile", System.StringComparison.OrdinalIgnoreCase)) mobile = true;
                if (string.Equals(a[i], "-seat", System.StringComparison.OrdinalIgnoreCase) && i + 1 < a.Length) int.TryParse(a[i + 1], out seat);
            }
            return mobile && seat > 0 ? BasePort + seat : 0;
        }

        /// <summary>"D FWD" → (true, W). 모르는 말이면 false.</summary>
        public static bool Parse(string msg, out bool down, out Key key)
        {
            down = false; key = Key.None;
            var p = msg.Trim().Split(' ');
            if (p.Length != 2 || (p[0] != "D" && p[0] != "U") || !Map.TryGetValue(p[1], out key)) return false;
            down = p[0] == "D";
            return true;
        }

        void Start()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;   // 창 포커스와 무관하게 받는다
            int port = Port();
            try { udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port)); }
            catch (SocketException e) { Debug.LogWarning($"[RemoteKeys] 127.0.0.1:{port} 열기 실패 — {e.Message}"); return; }
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "RemoteKeys" };
            thread.Start();
            Debug.Log($"[RemoteKeys] 리모컨 직접 입력 127.0.0.1:{port}");
        }

        void Run()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    var data = udp.Receive(ref from);
                    if (!Parse(Encoding.UTF8.GetString(data), out bool down, out Key key)) continue;
                    lock (gate) { if (down) held.Add(key); else held.Remove(key); dirty = true; }
                }
                catch (SocketException) { }
                catch (System.ObjectDisposedException) { return; }
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (!dirty || kb == null) return;
            Key[] keys;
            lock (gate) { keys = new Key[held.Count]; held.CopyTo(keys); dirty = false; }
            InputSystem.QueueStateEvent(kb, new KeyboardState(keys));
        }

        void OnDestroy()
        {
            running = false;
            try { udp?.Close(); } catch { }
        }
    }
}
