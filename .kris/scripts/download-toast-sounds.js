/**
 * Toast Sound Download Script
 *
 * Downloads free notification sounds from Mixkit and converts to MP3/OGG
 * Run: node scripts/download-toast-sounds.js
 */

import https from 'https';
import fs from 'fs';
import path from 'path';
import { exec } from 'child_process';
import { promisify } from 'util';
import { fileURLToPath } from 'url';
import { dirname } from 'path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

const execAsync = promisify(exec);

// Sound sources from Mixkit (Free, no attribution required)
const SOUNDS = {
  success: {
    url: 'https://assets.mixkit.co/active_storage/sfx/2000/2000-preview.mp3',
    name: 'Notification - Success',
    file: 'success.mp3',
  },
  error: {
    url: 'https://assets.mixkit.co/active_storage/sfx/2955/2955-preview.mp3',
    name: 'Error - Alert',
    file: 'error.mp3',
  },
  warning: {
    url: 'https://assets.mixkit.co/active_storage/sfx/2869/2869-preview.mp3',
    name: 'Warning - Alert',
    file: 'warning.mp3',
  },
  info: {
    url: 'https://assets.mixkit.co/active_storage/sfx/2354/2354-preview.mp3',
    name: 'Info - Notification',
    file: 'info.mp3',
  },
};

const OUTPUT_DIR = path.join(__dirname, '..', 'public', 'sounds', 'toast');

/**
 * Download a file from URL
 */
function downloadFile(url, outputPath) {
  return new Promise((resolve, reject) => {
    const file = fs.createWriteStream(outputPath);

    https.get(url, (response) => {
      if (response.statusCode === 302 || response.statusCode === 301) {
        // Follow redirect
        return downloadFile(response.headers.location, outputPath)
          .then(resolve)
          .catch(reject);
      }

      if (response.statusCode !== 200) {
        reject(new Error(`Failed to download: ${response.statusCode}`));
        return;
      }

      response.pipe(file);

      file.on('finish', () => {
        file.close();
        resolve();
      });
    }).on('error', (err) => {
      fs.unlink(outputPath, () => {}); // Clean up on error
      reject(err);
    });
  });
}

/**
 * Convert MP3 to OGG using ffmpeg
 */
async function convertToOgg(mp3Path, oggPath) {
  try {
    await execAsync(`ffmpeg -i "${mp3Path}" -c:a libvorbis -q:a 4 "${oggPath}" -y`);
    return true;
  } catch (error) {
    console.warn(`  ⚠️  ffmpeg not available. Skipping OGG conversion.`);
    console.warn(`     Install ffmpeg to generate OGG files: https://ffmpeg.org/download.html`);
    return false;
  }
}

/**
 * Check if ffmpeg is available
 */
async function checkFfmpeg() {
  try {
    await execAsync('ffmpeg -version');
    return true;
  } catch (error) {
    return false;
  }
}

/**
 * Main download process
 */
async function main() {
  console.log('🎵 Toast Sound Download Script');
  console.log('================================\n');

  // Create output directory if it doesn't exist
  if (!fs.existsSync(OUTPUT_DIR)) {
    fs.mkdirSync(OUTPUT_DIR, { recursive: true });
    console.log(`✅ Created directory: ${OUTPUT_DIR}\n`);
  }

  // Check for ffmpeg
  const hasFfmpeg = await checkFfmpeg();
  if (!hasFfmpeg) {
    console.log('⚠️  ffmpeg not found - will download MP3 only');
    console.log('   Install ffmpeg for OGG conversion: https://ffmpeg.org/download.html\n');
  }

  // Download each sound
  for (const [type, sound] of Object.entries(SOUNDS)) {
    console.log(`📥 Downloading: ${sound.name} (${type})`);

    const mp3Path = path.join(OUTPUT_DIR, sound.file);
    const oggPath = path.join(OUTPUT_DIR, sound.file.replace('.mp3', '.ogg'));

    try {
      // Download MP3
      await downloadFile(sound.url, mp3Path);
      console.log(`   ✅ Downloaded: ${sound.file}`);

      // Convert to OGG if ffmpeg is available
      if (hasFfmpeg) {
        await convertToOgg(mp3Path, oggPath);
        console.log(`   ✅ Converted: ${sound.file.replace('.mp3', '.ogg')}`);
      }

      console.log('');
    } catch (error) {
      console.error(`   ❌ Error: ${error.message}\n`);
    }
  }

  console.log('================================');
  console.log('✅ Sound download complete!\n');

  // Summary
  const files = fs.readdirSync(OUTPUT_DIR);
  console.log('📁 Files in public/sounds/toast/:');
  files.forEach(file => console.log(`   - ${file}`));

  if (!hasFfmpeg) {
    console.log('\n⚠️  Note: Only MP3 files were downloaded.');
    console.log('   Install ffmpeg and re-run to generate OGG files for broader browser support.');
  }

  console.log('\n🎉 Ready to test! Visit your app and trigger toast notifications.');
}

main().catch(console.error);
