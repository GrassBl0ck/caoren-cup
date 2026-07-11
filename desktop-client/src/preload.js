const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('caorenDesktop', Object.freeze({
  listSteamAccounts: () => ipcRenderer.invoke('caoren:steam:list'),
  selectSteamAccount: (accountRef) => ipcRenderer.invoke('caoren:steam:select', { accountRef }),
  authenticateDevice: (accountRef, purpose) => ipcRenderer.invoke('caoren:auth:login', { accountRef, purpose }),
  enrollDevice: (enrollmentCode) => ipcRenderer.invoke('caoren:auth:enroll', { enrollmentCode }),
  logoutDevice: () => ipcRenderer.invoke('caoren:auth:logout'),
}));
