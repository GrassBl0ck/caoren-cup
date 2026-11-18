const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('caorenDesktop', Object.freeze({
  authenticatePlayerCenter: () => ipcRenderer.invoke('caoren:player-center:auto-login'),
  loginPlayerCenter: (email, password, rememberDevice) => ipcRenderer.invoke('caoren:player-center:account-login', {
    email, password, rememberDevice: rememberDevice === true,
  }),
  clearRejectedDeviceCredential: () => ipcRenderer.invoke('caoren:player-center:clear-rejected-device'),
  connectGameServer: (connectUrl) => ipcRenderer.invoke('caoren:game:connect', { connectUrl }),
  logoutDevice: () => ipcRenderer.invoke('caoren:auth:logout'),
}));
