const fs = require('node:fs');
const path = require('node:path');
const { Arch, Platform, build } = require('electron-builder');

const projectDir = path.resolve(__dirname, '..');
const gallery = path.resolve(process.env.CAOREN_IMAGE_BUNDLE || path.join(projectDir, 'dist/image-bundle'));

async function configureWizard(projectFile) {
  const xml = await fs.promises.readFile(projectFile, 'utf8');
  // The stock wizard skips the folder page for a current-user install.
  const skipFolder = 'Value="VerifyReadyDlg" Order="6">WixAppFolder = "WixPerUserFolder"';
  if (!xml.includes(skipFolder)) throw new Error('WiX 安装向导模板已改变，请重新检查目录选择页。');
  const localized = xml.replace(skipFolder, skipFolder.replace('VerifyReadyDlg', 'InstallDirDlg'))
    .replace('Language="1033"', 'Language="2052"');
  await fs.promises.writeFile(projectFile, localized, 'utf8');
}

async function main() {
  const index = JSON.parse(fs.readFileSync(path.join(gallery, 'index.json'), 'utf8'));
  const size = fs.statSync(path.join(gallery, 'images.pack')).size;
  if (!size || size !== index.size || !Object.keys(index.images || {}).length) {
    throw new Error('图库不完整，请先生成完整包，或用 CAOREN_IMAGE_BUNDLE 指向已有 image-bundle 目录。');
  }
  await build({
    projectDir,
    targets: Platform.WINDOWS.createTarget(['msi'], Arch.x64),
    config: {
      directories: { output: path.join(projectDir, 'dist/installer') },
      electronDist: path.join(projectDir, 'node_modules/electron/dist'),
      extraFiles: [{ from: gallery, to: 'image-bundle', filter: ['images.pack', 'index.json'] }],
      msi: {
        artifactName: '草台杯客户端-安装程序-v${version}.${ext}',
        oneClick: false,
        runAfterFinish: false,
        additionalLightArgs: ['-cultures:zh-cn'],
      },
      msiProjectCreated: configureWizard,
    },
  });
}

main().catch(error => { console.error(error); process.exitCode = 1; });

