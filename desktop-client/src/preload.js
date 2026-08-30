const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('caorenDesktop', Object.freeze({
  authenticatePlayerCenter: () => ipcRenderer.invoke('caoren:player-center:auto-login'),
  loginPlayerCenter: (loginName, password, rememberDevice) => ipcRenderer.invoke('caoren:player-center:account-login', {
    loginName, password, rememberDevice: rememberDevice === true,
  }),
  clearRejectedDeviceCredential: () => ipcRenderer.invoke('caoren:player-center:clear-rejected-device'),
  logoutDevice: () => ipcRenderer.invoke('caoren:auth:logout'),
}));
