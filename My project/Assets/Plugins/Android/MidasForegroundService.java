// Arquivo: Assets/Plugins/Android/MidasForegroundService.java
//
// Este arquivo é compilado automaticamente pelo Unity durante o build Android.
// NÃO É um script C# — é código Java nativo que roda dentro do processo Android.
//
// Requisitos no AndroidManifest.xml (gerenciado pelo AndroidManifestMerge.xml):
//   <service android:name="com.luckarkman.xr.MidasForegroundService"
//            android:exported="false"
//            android:foregroundServiceType="location" />

package com.luckarkman.xr;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.IBinder;

/**
 * Foreground Service do Assistente MiDaS.
 *
 * Por que é necessário?
 *   - A partir do Android 8.0 (API 26), processos em background têm tempo de vida
 *     muito limitado (alguns segundos a minutos).
 *   - Um Foreground Service exibe uma notificação persistente e recebe prioridade
 *     especial do sistema, impedindo seu encerramento automático.
 *   - START_STICKY garante reinicialização automática se o sistema precisar
 *     encerrar o serviço por falta de memória RAM.
 */
public class MidasForegroundService extends Service {

    // ID do canal de notificação (obrigatório Android 8.0+).
    // Deve ser único no app — usamos o namespace do projeto.
    private static final String CHANNEL_ID = "com.luckarkman.xr.assistant";

    // Nome legível do canal exibido nas Configurações do Android.
    private static final String CHANNEL_NAME = "Assistente de Navegação";

    // ID numérico da notificação. Deve ser > 0 e único para este serviço.
    private static final int NOTIFICATION_ID = 7001;

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {

        // Passo 1: Cria o canal de notificação (necessário no Android 8.0+).
        // Em versões anteriores, esta chamada não faz nada.
        createNotificationChannel();

        // Passo 2: Constrói a notificação que ficará visível na barra de status.
        Notification notification = buildNotification();

        // Passo 3: Promove este serviço para Foreground.
        // Android 14 (API 34) exige que o tipo do serviço seja declarado
        // tanto no manifest quanto na chamada startForeground().
        if (Build.VERSION.SDK_INT >= 34) {
            // Verifica se a permissão foi concedida. Se não, usa o tipo padrão para evitar crash.
            if (checkSelfPermission(android.Manifest.permission.ACCESS_FINE_LOCATION) 
                == android.content.pm.PackageManager.PERMISSION_GRANTED) {
                startForeground(NOTIFICATION_ID, notification,
                        ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION);
            } else {
                // Fallback de segurança: inicia sem o tipo restrito para evitar o crash imediato.
                // O sistema pode encerrar o serviço depois, mas o app não fecha na hora.
                startForeground(NOTIFICATION_ID, notification);
            }
        } else {
            startForeground(NOTIFICATION_ID, notification);
        }

        // START_STICKY: se o sistema encerrar o serviço por falta de memória,
        // ele será reiniciado automaticamente assim que houver recursos disponíveis.
        // O Intent pode ser null na reinicialização — tratamos isso via null check no Intent.
        return START_STICKY;
    }

    /**
     * Cria o NotificationChannel obrigatório para Android 8.0+.
     * Em versões anteriores, este método é executado mas não faz nada.
     *
     * IMPORTANCE_LOW: Não emite som nem vibração — apenas mantém
     * a notificação visível sem incomodar o usuário.
     */
    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    CHANNEL_NAME,
                    NotificationManager.IMPORTANCE_LOW
            );
            channel.setDescription("Mantém o assistente de navegação ativo em segundo plano.");
            channel.setShowBadge(false); // Não exibe badge no ícone do app

            NotificationManager manager =
                    (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);

            if (manager != null) {
                manager.createNotificationChannel(channel);
            }
        }
    }

    /**
     * Constrói a notificação persistente do serviço.
     * Usa Notification.Builder nativo (sem dependência de support library)
     * para compatibilidade com o ambiente de build do Unity.
     */
    private Notification buildNotification() {

        // Android 8.0+ usa o construtor com channelId.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            return new Notification.Builder(this, CHANNEL_ID)
                    .setContentTitle("Assistente Ativo")
                    .setContentText("O Pequeno Príncipe está guiando você...")
                    .setSmallIcon(android.R.drawable.ic_menu_compass)
                    .setOngoing(true)      // Impede que o usuário deslize para fechar
                    .setShowWhen(false)    // Não exibe o horário na notificação
                    .build();
        } else {
            // Android < 8.0: construtor sem channelId
            // Suprimimos o warning de deprecação pois é necessário para compatibilidade.
            @SuppressWarnings("deprecation")
            Notification notification = new Notification.Builder(this)
                    .setContentTitle("Assistente Ativo")
                    .setContentText("O Pequeno Príncipe está guiando você...")
                    .setSmallIcon(android.R.drawable.ic_menu_compass)
                    .setOngoing(true)
                    .setShowWhen(false)
                    .build();
            return notification;
        }
    }

    /**
     * Chamado quando o app é explicitamente encerrado pelo usuário.
     * Remove a notificação e o status de Foreground.
     */
    @Override
    public void onDestroy() {
        // Remove a notificação e rebaixa o serviço de Foreground.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            stopForeground(STOP_FOREGROUND_REMOVE); // API 24+: constante explícita
        } else {
            //noinspection deprecation
            stopForeground(true); // Remove a notificação
        }
        super.onDestroy();
    }

    /**
     * Este serviço NÃO suporta binding (comunicação direta com outros componentes).
     * Toda comunicação com a Unity ocorre via UnitySendMessage quando necessário.
     */
    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}
