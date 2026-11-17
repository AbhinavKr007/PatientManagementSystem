# LAN Screen Share

Show a Windows PC's screen live on an iPad, an Android phone/tablet, or any
other device on the same Wi-Fi. Nothing is installed on the viewing device –
it just opens a web page. Video goes directly between the devices (WebRTC) and
never leaves your network.

## Use it

**On the Windows PC (once):** install [Node.js LTS](https://nodejs.org).

1. Double-click `start.bat`. A browser tab opens at `http://localhost:8080`.
   (Or run `npm install` and `npm start`, then open that address in Chrome/Edge.)
2. Click **Start sharing** and pick the screen, window or tab.
3. **On the iPad / Android device:** scan the QR code shown on the PC with the
   camera, or type the address shown (e.g. `http://192.168.1.20:8080`) into
   Safari / Chrome and enter the 4-digit PIN.

Any number of devices can watch at once. Viewers reconnect on their own if you
stop and restart sharing.

- **Sound:** choose "Entire screen" or a browser tab and tick "Also share
  audio" in the picker; on the tablet tap **Tap for sound**.
- **Full screen:** tap the screen, then **Full screen**. On iPad you can also
  use Share → "Add to Home Screen" to get an app-like icon without Safari bars.
- **Quality:** the selector on the PC trades sharpness against smoothness.

## If the tablet cannot connect

- Both devices must be on the same Wi-Fi (not a "guest" network, which usually
  isolates devices from each other).
- The first time, Windows Firewall asks about Node.js – choose **Allow** for
  private networks. If you dismissed it, allow "Node.js JavaScript Runtime" in
  *Windows Security → Firewall → Allow an app through firewall*.
- If the PC shows several addresses, pick another from the dropdown under the
  QR code (VPN and virtual adapters add extra ones).

## Settings

| Variable | Default | Meaning |
| --- | --- | --- |
| `PORT` | `8080` | Port to listen on |
| `PIN` | random each start | Fixed PIN for viewers |

Example: `set PIN=2468 && set PORT=9090 && node server.js`

## Limits

- Only the PC running the server can share; the page at `http://localhost` is
  the only one allowed to capture. Other devices are viewers.
- iPad and Android are **viewers**. Sending an iPad's or Android device's own
  screen is not possible from a web page (mobile browsers do not expose screen
  capture) and would need a native app.
- The connection is plain HTTP inside your LAN, protected by the PIN. Use it on
  networks you trust.
