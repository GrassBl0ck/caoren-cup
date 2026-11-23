const path = require('node:path');
const { execFileSync } = require('node:child_process');

module.exports = async ({ artifactPaths }) => {
  const executables = artifactPaths.filter(file => path.extname(file).toLowerCase() === '.exe');
  if (executables.length !== 1) throw new Error('完整包需要一个 Windows 便携版 EXE。');
  const [executable] = executables;
  const outputDir = path.dirname(executable);
  const archive = path.join(outputDir, path.basename(executable, '.exe') + '-完整包.zip');
  const gallery = path.join(outputDir, 'image-bundle');
  const root = process.env.CAOREN_PUBLIC_IMAGES || path.resolve(__dirname, '../../web-command-center/public');
  const python = process.env.PYTHON || 'python';
  execFileSync(python, [path.join(__dirname, 'pack-images.py'), root, gallery], { stdio: 'inherit' });
  execFileSync(python, [path.join(__dirname, 'package-full.py'), executable, gallery, archive], { stdio: 'inherit' });
  return [archive];
};
