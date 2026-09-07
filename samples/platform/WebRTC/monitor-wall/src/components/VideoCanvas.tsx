import React, { useRef, useEffect } from 'react';
import { CodecMode } from '@webrtc-broadcast/shared';

interface VideoCanvasProps {
  cameraId: string;
  cameraName: string;
  online: boolean;
  codec: CodecMode;
  fps: number;
}

export const VideoCanvas: React.FC<VideoCanvasProps> = ({
  cameraId,
  cameraName,
  online,
  codec,
  fps,
}) => {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    let animId: number;
    let frameCount = 0;

    const render = () => {
      const width = canvas.width;
      const height = canvas.height;

      if (!online) {
        // Offline / No signal card
        ctx.fillStyle = '#06070a';
        ctx.fillRect(0, 0, width, height);

        // SMPTE bars muted
        const colors = ['#333', '#444', '#222', '#111', '#555', '#222', '#000'];
        const barW = width / colors.length;
        colors.forEach((c, i) => {
          ctx.fillStyle = c;
          ctx.fillRect(i * barW, 0, barW, height * 0.7);
        });

        // Center badge
        ctx.fillStyle = 'rgba(0,0,0,0.85)';
        ctx.fillRect(width / 2 - 80, height / 2 - 25, 160, 50);
        ctx.strokeStyle = 'rgba(255,255,255,0.2)';
        ctx.strokeRect(width / 2 - 80, height / 2 - 25, 160, 50);

        ctx.fillStyle = '#ff2a51';
        ctx.font = 'bold 14px "JetBrains Mono", monospace';
        ctx.textAlign = 'center';
        ctx.fillText('SIGNAL LOST', width / 2, height / 2 + 5);
        return;
      }

      frameCount++;

      // 1. Draw stadium field / broadcast background simulation
      // Subtle gradient base
      const camNum = parseInt(cameraId.replace('cam-', ''), 10) || 1;
      const hue = 210 + (camNum * 12) % 60; // Deep broadcast blues and teals
      
      const grad = ctx.createLinearGradient(0, 0, width, height);
      grad.addColorStop(0, `hsl(${hue}, 35%, 12%)`);
      grad.addColorStop(1, `hsl(${hue + 20}, 45%, 6%)`);
      ctx.fillStyle = grad;
      ctx.fillRect(0, 0, width, height);

      // 2. Perspective grid / stadium court lines simulation
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.06)';
      ctx.lineWidth = 1;
      for (let x = 0; x < width; x += 40) {
        ctx.beginPath();
        ctx.moveTo(x, 0);
        ctx.lineTo(x, height);
        ctx.stroke();
      }
      for (let y = 0; y < height; y += 30) {
        ctx.beginPath();
        ctx.moveTo(0, y);
        ctx.lineTo(width, y);
        ctx.stroke();
      }

      // 3. Dynamic Motion Sweeper (Radar / PTZ tracking circle)
      const centerX = width / 2;
      const centerY = height / 2;
      const t = Date.now() / 1000;
      const sweepAngle = (t * 2 + camNum) % (Math.PI * 2);

      ctx.save();
      ctx.beginPath();
      ctx.arc(centerX, centerY, Math.min(width, height) * 0.35, 0, Math.PI * 2);
      ctx.strokeStyle = 'rgba(0, 229, 255, 0.18)';
      ctx.lineWidth = 1.5;
      ctx.stroke();

      // Sweeping line
      ctx.beginPath();
      ctx.moveTo(centerX, centerY);
      ctx.lineTo(
        centerX + Math.cos(sweepAngle) * (width * 0.35),
        centerY + Math.sin(sweepAngle) * (height * 0.35)
      );
      ctx.strokeStyle = 'rgba(0, 229, 255, 0.45)';
      ctx.stroke();
      ctx.restore();

      // 4. Moving target tracker (simulating player / ball tracking box)
      const targetX = centerX + Math.sin(t * 1.5 + camNum) * (width * 0.28);
      const targetY = centerY + Math.cos(t * 1.2 + camNum) * (height * 0.22);
      ctx.strokeStyle = 'rgba(255, 214, 0, 0.7)';
      ctx.lineWidth = 1.5;
      ctx.strokeRect(targetX - 16, targetY - 16, 32, 32);

      // Target crosshairs
      ctx.beginPath();
      ctx.moveTo(targetX - 6, targetY);
      ctx.lineTo(targetX + 6, targetY);
      ctx.moveTo(targetX, targetY - 6);
      ctx.lineTo(targetX, targetY + 6);
      ctx.stroke();

      // 5. Broadcast SMPTE Timecode Counter (HH:MM:SS:FF @ 60fps)
      const now = new Date();
      const hours = String(now.getHours()).padStart(2, '0');
      const minutes = String(now.getMinutes()).padStart(2, '0');
      const seconds = String(now.getSeconds()).padStart(2, '0');
      const frameRate = fps || 60;
      const frames = String(Math.floor((now.getMilliseconds() / 1000) * frameRate)).padStart(2, '0');
      const smpte = `${hours}:${minutes}:${seconds}:${frames}`;

      // Draw SMPTE timecode box bottom center
      ctx.fillStyle = 'rgba(0, 0, 0, 0.75)';
      ctx.fillRect(width / 2 - 65, height - 32, 130, 22);
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.12)';
      ctx.strokeRect(width / 2 - 65, height - 32, 130, 22);

      ctx.fillStyle = '#f3f4f8';
      ctx.font = 'bold 11px "JetBrains Mono", monospace';
      ctx.textAlign = 'center';
      ctx.fillText(smpte, width / 2, height - 17);

      // 6. Safe Area Markers (Broadcast 90% action safe & 80% title safe)
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.05)';
      ctx.lineWidth = 1;
      ctx.strokeRect(width * 0.05, height * 0.05, width * 0.9, height * 0.9);
      ctx.strokeRect(width * 0.1, height * 0.1, width * 0.8, height * 0.8);

      animId = requestAnimationFrame(render);
    };

    render();

    return () => {
      cancelAnimationFrame(animId);
    };
  }, [cameraId, cameraName, online, codec, fps]);

  return (
    <div className="canvas-wrapper">
      <canvas
        ref={canvasRef}
        width={640}
        height={360}
        className="camera-canvas"
      />
    </div>
  );
};
