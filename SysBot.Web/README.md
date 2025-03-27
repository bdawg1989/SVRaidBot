# SysBot.Web - Web-API und Fernbedienung für SysBot

Diese Komponente ermöglicht es, den Bot über eine Web-Oberfläche zu steuern und zu überwachen, auch von außerhalb des lokalen Netzwerks.

## Funktionen

- RESTful API zur Steuerung des Bots (Start, Stop, Idle, Resume, Reboot)
- Globale Steuerelemente (Alle Bots starten, Alle Bots stoppen)
- Status-Überwachung aller konfigurierten Bots
- Zugriff auf Bot-Logs
- Einfache Web-Oberfläche für mobile Geräte und Desktop-Browser

## Einrichtung auf dem Windows-PC

Die Web-API wird automatisch als Teil der SysBot.Pokemon.WinForms Anwendung gestartet.
Standardmäßig wird sie auf Port 6500 betrieben.

Um die API von außerhalb deines lokalen Netzwerks zugänglich zu machen, musst du:

1. Eine Port-Weiterleitung in deinem Router einrichten (Port 6500 nach innen zum PC mit dem Bot)
2. Deine Windows-Firewall konfigurieren, um eingehende Verbindungen auf Port 6500 zuzulassen

## Einrichtung auf dem Linux-Server

Die im Projekt enthaltene Web-Oberfläche kann auf deinem Linux-Server gehostet werden. Hier die Schritte:

### 1. Kopiere die HTML-Datei

Kopiere die Datei `wwwroot/index.html` in das Webverzeichnis deines Linux-Servers (z.B. `/var/www/html/`).

### 2. Konfiguriere die API-URL

Bearbeite die HTML-Datei auf deinem Linux-Server und ändere die IP-Adresse in der Konfiguration:

```javascript
const CONFIG = {
    apiUrl: 'http://DEINE-ÖFFENTLICHE-IP:6500/api', // Ändere dies zur öffentlichen IP oder Domain deines Windows-PCs
    refreshInterval: 5000 // Aktualisierungsintervall in Millisekunden
};
```

### 3. Konfiguriere Nginx als Reverse-Proxy (optional, aber empfohlen)

Installiere Nginx:
```bash
sudo apt update
sudo apt install nginx
```

Erstelle eine Nginx-Konfiguration (z.B. `/etc/nginx/sites-available/sysbot`):
```nginx
server {
    listen 80;
    server_name sysbot.deinedomain.de;  # Oder deine IP-Adresse

    location / {
        root /var/www/html;  # Dein Webverzeichnis
        index index.html;
    }

    # Optional: SSL-Konfiguration mit Let's Encrypt
    # listen 443 ssl;
    # ssl_certificate /etc/letsencrypt/live/sysbot.deinedomain.de/fullchain.pem;
    # ssl_certificate_key /etc/letsencrypt/live/sysbot.deinedomain.de/privkey.pem;
}
```

Aktiviere die Konfiguration:
```bash
sudo ln -s /etc/nginx/sites-available/sysbot /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx
```

### 4. SSL einrichten (optional, aber empfohlen)

Du kannst Let's Encrypt für ein kostenloses SSL-Zertifikat verwenden:
```bash
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d sysbot.deinedomain.de
```

## Erweiterte Funktionen

Die Web-Oberfläche bietet folgende Steuerungselemente:

### Globale Steuerung
- **Alle Bots starten**: Startet alle konfigurierten Bots gleichzeitig
- **Alle Bots stoppen**: Stoppt alle laufenden Bots

### Bot-spezifische Steuerung
- **Start**: Startet einen einzelnen Bot
- **Stop**: Stoppt einen einzelnen Bot
- **Pause**: Setzt einen Bot in den Leerlauf
- **Fortsetzen**: Setzt einen pausierten Bot fort
- **Neustart & Stop**: Führt einen Reset der Verbindung durch und stoppt den Bot

## Sicherheitshinweise

- **Die Web-API hat standardmäßig keine Authentifizierung!** Es wird empfohlen, einen Reverse-Proxy mit Passwortschutz zu verwenden.
- Verwende immer HTTPS, wenn du von außerhalb auf die API zugreifst.
- Verwende eine starke Firewall-Konfiguration und öffne nur die notwendigen Ports.

## Fehlerbehebung

- Wenn die Web-API nicht erreichbar ist, überprüfe:
  - Läuft der Bot auf dem Windows-PC?
  - Ist der Port 6500 in der Windows-Firewall geöffnet?
  - Ist die Port-Weiterleitung im Router korrekt konfiguriert?
  - Ist die IP-Adresse in der Web-Oberfläche korrekt?

- Wenn die Logs nicht geladen werden:
  - Prüfe, ob das Log-Verzeichnis existiert und Leserechte hat 