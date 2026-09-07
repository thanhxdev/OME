import fs from 'fs';
import path from 'path';
import crypto from 'crypto';
import http from 'http';
import { RecordedSegment } from './recorder';
import { config } from './config';

export interface UploadRecord {
  id: string;
  cameraId: string;
  filename: string;
  filePath: string;
  fileSizeBytes: number;
  durationSeconds: number;
  sha256: string;
  status: 'pending' | 'uploading' | 'completed' | 'failed' | 'local_archive';
  s3Url?: string;
  uploadedAt?: number;
  error?: string;
}

export class StorageUploader {
  private queue: RecordedSegment[] = [];
  private records: Map<string, UploadRecord> = new Map();
  private isProcessing = false;

  constructor() {
    // Scan existing files on startup
    this.scanExistingFiles();
  }

  private scanExistingFiles(): void {
    if (!fs.existsSync(config.recordingsDir)) return;
    try {
      const files = fs.readdirSync(config.recordingsDir);
      for (const file of files) {
        if (file.endsWith('.mp4') || file.endsWith('.ts')) {
          const filePath = path.join(config.recordingsDir, file);
          const stats = fs.statSync(filePath);
          const camId = file.split('_')[0] || 'unknown';

          this.records.set(file, {
            id: file,
            cameraId: camId,
            filename: file,
            filePath,
            fileSizeBytes: stats.size,
            durationSeconds: 0,
            sha256: 'cached',
            status: 'local_archive',
            uploadedAt: stats.mtimeMs,
          });
        }
      }
    } catch {}
  }

  public enqueue(segment: RecordedSegment): void {
    const id = segment.filename;
    this.records.set(id, {
      id,
      cameraId: segment.cameraId,
      filename: segment.filename,
      filePath: segment.filePath,
      fileSizeBytes: segment.fileSizeBytes,
      durationSeconds: segment.durationSeconds,
      sha256: 'calculating...',
      status: 'pending',
    });

    this.queue.push(segment);
    this.processQueue();
  }

  private async processQueue(): Promise<void> {
    if (this.isProcessing || this.queue.length === 0) return;
    this.isProcessing = true;

    while (this.queue.length > 0) {
      const segment = this.queue.shift()!;
      await this.uploadSegment(segment);
    }

    this.isProcessing = false;
  }

  private async uploadSegment(segment: RecordedSegment): Promise<void> {
    const record = this.records.get(segment.filename);
    if (!record) return;

    record.status = 'uploading';

    try {
      // 1. Calculate SHA-256 checksum
      const hash = await this.computeSha256(segment.filePath);
      record.sha256 = hash;

      // 2. Generate sidecar JSON metadata
      const meta = {
        cameraId: segment.cameraId,
        filename: segment.filename,
        durationSeconds: segment.durationSeconds,
        fileSizeBytes: segment.fileSizeBytes,
        partNumber: segment.partNumber,
        sha256: hash,
        recordedAt: new Date().toISOString(),
      };
      const metaPath = `${segment.filePath}.meta.json`;
      fs.writeFileSync(metaPath, JSON.stringify(meta, null, 2));

      // 3. Perform upload to S3/MinIO bucket
      if (config.autoUploadS3) {
        const s3Success = await this.uploadToS3(segment.filePath, segment.filename, segment.cameraId);
        if (s3Success) {
          record.status = 'completed';
          record.s3Url = `${config.s3Endpoint}/${config.s3Bucket}/${segment.cameraId}/${segment.filename}`;
          record.uploadedAt = Date.now();
        } else {
          record.status = 'local_archive';
          record.uploadedAt = Date.now();
        }
      } else {
        record.status = 'local_archive';
        record.uploadedAt = Date.now();
      }
    } catch (err: any) {
      record.status = 'failed';
      record.error = err.message || 'Upload error';
    }
  }

  private computeSha256(filePath: string): Promise<string> {
    return new Promise((resolve) => {
      try {
        if (!fs.existsSync(filePath)) return resolve('file_not_found');
        const hash = crypto.createHash('sha256');
        const stream = fs.createReadStream(filePath);
        stream.on('data', (chunk) => hash.update(chunk));
        stream.on('end', () => resolve(hash.digest('hex')));
        stream.on('error', () => resolve('read_error'));
      } catch {
        resolve('error');
      }
    });
  }

  private uploadToS3(filePath: string, filename: string, cameraId: string): Promise<boolean> {
    return new Promise((resolve) => {
      try {
        if (!fs.existsSync(filePath)) return resolve(false);

        const u = new URL(`${config.s3Endpoint}/${config.s3Bucket}/${cameraId}/${filename}`);
        const stats = fs.statSync(filePath);

        const req = http.request(
          {
            hostname: u.hostname,
            port: u.port,
            path: u.pathname,
            method: 'PUT',
            headers: {
              'Content-Type': 'video/mp4',
              'Content-Length': stats.size,
            },
            timeout: 5000,
          },
          (res) => {
            if (res.statusCode && res.statusCode >= 200 && res.statusCode < 300) {
              resolve(true);
            } else {
              resolve(false);
            }
          }
        );

        req.on('error', () => resolve(false));
        const fileStream = fs.createReadStream(filePath);
        fileStream.pipe(req);
      } catch {
        resolve(false);
      }
    });
  }

  public getRecords(): UploadRecord[] {
    return Array.from(this.records.values()).sort(
      (a, b) => (b.uploadedAt || 0) - (a.uploadedAt || 0)
    );
  }
}
