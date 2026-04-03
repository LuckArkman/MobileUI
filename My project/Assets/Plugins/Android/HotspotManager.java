// Arquivo: Assets/Plugins/Android/HotspotManager.java
//
// Gerencia o ciclo de vida do Local-Only Hotspot Android.
// Não pode ser substituído por AndroidJavaProxy porque
// WifiManager.LocalOnlyHotspotCallback é uma CLASSE ABSTRATA, não interface.
// AndroidJavaProxy só funciona com interfaces Java.
//
// API mínima: Android 8.0 (API 26) para startLocalOnlyHotspot.

package com.luckarkman.xr;

import android.content.Context;
import android.net.wifi.WifiManager;
import android.os.Build;
import com.unity3d.player.UnityPlayer;

public class HotspotManager {

    private static HotspotManager instance;

    private WifiManager wifiManager;
    private WifiManager.LocalOnlyHotspotReservation reservation;
    private String unityGameObjectName;
    private boolean isActive = false;

    // ── Singleton ─────────────────────────────────────────────────────────

    public static HotspotManager getInstance() {
        if (instance == null) {
            instance = new HotspotManager();
        }
        return instance;
    }

    private HotspotManager() {}

    // ── API Pública ────────────────────────────────────────────────────────

    /**
     * Inicia o Local-Only Hotspot Android.
     *
     * @param context           Contexto Android (Activity).
     * @param gameObjectName    Nome do GameObject Unity para callbacks via UnitySendMessage.
     */
    public void startHotspot(Context context, String gameObjectName) {
        this.unityGameObjectName = gameObjectName;

        if (Build.VERSION.SDK_INT < 26) {
            // API 26 é o mínimo para startLocalOnlyHotspot
            UnityPlayer.UnitySendMessage(gameObjectName, "OnHotspotFailed", "API_TOO_LOW");
            return;
        }

        if (isActive) {
            UnityPlayer.UnitySendMessage(gameObjectName, "OnHotspotStarted", "ALREADY_ACTIVE");
            return;
        }

        try {
            wifiManager = (WifiManager) context.getApplicationContext()
                    .getSystemService(Context.WIFI_SERVICE);

            if (wifiManager == null) {
                UnityPlayer.UnitySendMessage(gameObjectName, "OnHotspotFailed", "WIFI_MANAGER_NULL");
                return;
            }

            // startLocalOnlyHotspot(callback, handler)
            // handler = null → callback executa na thread principal do processo
            wifiManager.startLocalOnlyHotspot(hotspotCallback, null);

        } catch (Exception e) {
            UnityPlayer.UnitySendMessage(gameObjectName, "OnHotspotFailed", e.getMessage());
        }
    }

    /**
     * Encerra o hotspot e libera a reserva.
     */
    public void stopHotspot() {
        if (reservation != null) {
            reservation.close();
            reservation = null;
        }
        isActive = false;
    }

    public boolean isHotspotActive() {
        return isActive;
    }

    // ── Callback do Hotspot ────────────────────────────────────────────────

    private final WifiManager.LocalOnlyHotspotCallback hotspotCallback =
            new WifiManager.LocalOnlyHotspotCallback() {

        @Override
        public void onStarted(WifiManager.LocalOnlyHotspotReservation r) {
            reservation = r;
            isActive = true;

            // Tenta obter o SSID da configuração do hotspot
            String ssidInfo = "HOTSPOT_ATIVO";
            if (Build.VERSION.SDK_INT >= 26 && r.getWifiConfiguration() != null) {
                ssidInfo = r.getWifiConfiguration().SSID;
            }

            UnityPlayer.UnitySendMessage(
                    instance.unityGameObjectName, "OnHotspotStarted", ssidInfo);
        }

        @Override
        public void onStopped() {
            reservation = null;
            isActive = false;
            UnityPlayer.UnitySendMessage(
                    instance.unityGameObjectName, "OnHotspotStopped", "");
        }

        @Override
        public void onFailed(int reason) {
            isActive = false;
            // Reason codes: ERROR_NO_CHANNEL=1, ERROR_GENERIC=2, ERROR_INCOMPATIBLE_MODE=3, ERROR_TETHERING_DISALLOWED=4
            UnityPlayer.UnitySendMessage(
                    instance.unityGameObjectName, "OnHotspotFailed", String.valueOf(reason));
        }
    };
}
