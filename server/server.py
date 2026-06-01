import socket
import struct
import threading
import queue
import json
import numpy as np
import cv2
import os
from datetime import datetime
from ultralytics import YOLO

# ─── CONFIG ───────────────────────────────────────────────
HOST        = '0.0.0.0'
PORT        = 9999
MODEL_PATH  = "bestd.pt"
CONFIDENCE  = 0.5
DEVICE      = "mps"
IMGSZ       = 640
SKIP_FRAMES = 1
SAVE_VIDEO  = True
# ──────────────────────────────────────────────────────────

# ─── YOLO SETUP ───────────────────────────────────────────
model = YOLO(MODEL_PATH)
model.fuse()

roi_main    = (220, 160, 440, 310)
roi_traffic = (260,  60, 400, 280)
# ──────────────────────────────────────────────────────────

# ─── LANE DETECTION SETUP ─────────────────────────────────
tl = (380, 280)
bl = ( 60, 540)
tr = (680, 280)
br = (960, 540)

pts1 = np.float32([tl, bl, tr, br])
pts2 = np.float32([[0, 0], [0, 540], [960, 0], [960, 540]])
M    = cv2.getPerspectiveTransform(pts1, pts2)
Minv = cv2.getPerspectiveTransform(pts2, pts1)
# ──────────────────────────────────────────────────────────

# ─── VIDEO WRITERS ────────────────────────────────────────
session_dir  = f"recordings/{datetime.now().strftime('%Y%m%d_%H%M%S')}"
os.makedirs(session_dir, exist_ok=True)
video_writers = {}   # window_name → VideoWriter  (created lazily)

def get_writer(window_name, frame):
    if window_name not in video_writers:
        h, w  = frame.shape[:2]
        safe  = window_name.replace(" ", "_").replace("|", "").replace("'", "").strip()
        path  = os.path.join(session_dir, f"{safe}.mp4")
        fourcc = cv2.VideoWriter_fourcc(*'mp4v')
        video_writers[window_name] = cv2.VideoWriter(path, fourcc, 10.0, (w, h))
        print(f"[REC] {window_name} → {path}")
    return video_writers[window_name]

def release_all_writers():
    for name, writer in video_writers.items():
        writer.release()
        print(f"[REC] Saved → {name}")
    video_writers.clear()
# ──────────────────────────────────────────────────────────

# ─── DISPLAY QUEUE ────────────────────────────────────────
display_queue = queue.Queue(maxsize=1)
# ──────────────────────────────────────────────────────────

# ─── SEND RESPONSE TO UNITY ───────────────────────────────
def send_response(conn, detections, confidence_score):
    payload = {
        "confidence": round(float(confidence_score), 4),
        "detections": [
            {"cls": d["class"], "confidence": round(float(d["confidence"]), 4)}
            for d in detections
        ]
    }
    data   = json.dumps(payload).encode("utf-8")
    header = struct.pack(">I", len(data))
    try:
        conn.sendall(header + data)
    except Exception as e:
        print(f"[!] Send error: {e}")

# ─── OBJECT DETECTION ─────────────────────────────────────
def run_object_detection(frame):
    try:
        frame     = cv2.resize(frame, (640, 360))
        results   = model(frame, conf=CONFIDENCE, device=DEVICE, verbose=False, imgsz=IMGSZ)
        annotated = results[0].plot()
        overlay   = annotated.copy()
        overlay_traffic = annotated.copy()

        x1, y1, x2, y2 = roi_main
        x3, y3, x4, y4 = roi_traffic
        cv2.rectangle(overlay, (x1, y1), (x2, y2), (255, 0, 0), 2)
        cv2.rectangle(overlay_traffic, (x3, y3), (x4, y4), (255, 255, 0), 2)

        dark = np.zeros_like(overlay)
        dim  = cv2.addWeighted(overlay, 0.3, dark, 0.7, 0)
        overlay[0:y1, :]  = dim[0:y1, :]
        overlay[y2:, :]   = dim[y2:, :]
        overlay[:, 0:x1]  = dim[:, 0:x1]
        overlay[:, x2:]   = dim[:, x2:]
        
        dark_traffic = np.zeros_like(overlay_traffic)
        dim_traffic  = cv2.addWeighted(overlay_traffic, 0.3, dark_traffic, 0.7, 0)
        overlay_traffic[0:y3, :]  = dim_traffic[0:y3, :]
        overlay_traffic[y4:, :]   = dim_traffic[y4:, :]
        overlay_traffic[:, 0:x3]  = dim_traffic[:, 0:x3]
        overlay_traffic[:, x4:]   = dim_traffic[:, x4:]

        detections = []

        for box in results[0].boxes:
            cls_name   = model.names[int(box.cls[0])]
            confidence = float(box.conf[0])
            bx1, by1, bx2, by2 = map(int, box.xyxy[0])

            cx = (bx1 + bx2) // 2
            cy = (by1 + by2) // 2

            if cls_name == "Pedistrian" or cls_name == 'Car':
                x1, y1, x2, y2 = roi_main
            else:
                x1, y1, x2, y2 = roi_traffic

            if x1 <= cx <= x2 and y1 <= cy <= y2:
                detections.append({"class": cls_name, "confidence": confidence})
            else:
                cv2.rectangle(annotated, (bx1, by1), (bx2, by2), (40, 40, 40), 2)
                label = f"{cls_name} {confidence:.2f}"
                (lw, lh), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.55, 1)
                cv2.rectangle(annotated, (bx1, by1 - lh - 8), (bx1 + lw, by1), (40, 40, 40), -1)
                cv2.putText(annotated, label, (bx1, by1 - 4),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.55, (80, 80, 80), 1, cv2.LINE_AA)

        roi_crop = annotated[y1:y2, x1:x2].copy()
        roi_crop_traffic = annotated[y3:y4, x3:x4].copy()

        debug_frames = {
            "ROI Overlay": overlay,
            "ROI Overlay_traffic": overlay_traffic,
            "ROI Crop": roi_crop,
            "ROI Crop_traffic": roi_crop_traffic,
        }

        # for d in detections:
        #     print(f"[DETECT] {d['class']} ({d['confidence']:.2f})")

        return annotated, debug_frames, detections

    except Exception as e:
        print(f"[!] Detection error: {e}")
        return frame, {}, []

# ─── LANE DETECTION ───────────────────────────────────────
def run_lane_detection(frame):
    frame    = cv2.resize(frame, (960, 540))
    original = frame.copy()

    confidence = 0

    debug_frame = original.copy()
    for p in [tl, bl, tr, br]:
        cv2.circle(debug_frame, p, 8, (0, 0, 255), -1)
    for a, b in [(tl, tr), (tr, br), (br, bl), (bl, tl)]:
        cv2.line(debug_frame, a, b, (255, 0, 0), 2)

    birdseye = cv2.warpPerspective(frame, M, (960, 540))
    hsv      = cv2.cvtColor(birdseye, cv2.COLOR_BGR2HSV)

    mask_white  = cv2.inRange(hsv, np.array([108,  30, 170]), np.array([118,  70, 255]))
    mask_yellow = cv2.inRange(hsv, np.array([ 15,  80,  80]), np.array([ 35, 255, 255]))
    combined_mask = cv2.bitwise_or(mask_white, mask_yellow)

    img_h, img_w = combined_mask.shape[:2]

    histogram_white  = np.sum(mask_white[img_h // 2:,  :], axis=0)
    histogram_yellow = np.sum(mask_yellow[img_h // 2:, :], axis=0)

    midpoint    = img_w // 2
    leftx_base  = int(np.argmax(histogram_white[:midpoint]))
    rightx_base = int(np.argmax(histogram_yellow[midpoint:])) + midpoint

    nwindows      = 12
    window_height = int(img_h / nwindows)
    margin        = 60
    minpix        = 100

    nonzeroy_white,  nonzerox_white  = mask_white.nonzero()
    nonzeroy_yellow, nonzerox_yellow = mask_yellow.nonzero()

    leftx_current  = leftx_base
    rightx_current = rightx_base
    left_lane_inds  = []
    right_lane_inds = []

    out_img = np.dstack((combined_mask, combined_mask, combined_mask))

    for window in range(nwindows):
        win_y_low  = img_h - (window + 1) * window_height
        win_y_high = img_h - window * window_height

        win_xleft_low   = leftx_current  - margin
        win_xleft_high  = leftx_current  + margin
        win_xright_low  = rightx_current - margin
        win_xright_high = rightx_current + margin

        cv2.rectangle(out_img,
                      (win_xleft_low,  win_y_low), (win_xleft_high,  win_y_high),
                      (0, 255, 0), 2)
        cv2.rectangle(out_img,
                      (win_xright_low, win_y_low), (win_xright_high, win_y_high),
                      (255, 0, 0), 2)

        good_left = ((nonzeroy_white >= win_y_low)    &
                     (nonzeroy_white <  win_y_high)   &
                     (nonzerox_white >= win_xleft_low) &
                     (nonzerox_white <  win_xleft_high)).nonzero()[0]

        good_right = ((nonzeroy_yellow >= win_y_low)       &
                      (nonzeroy_yellow <  win_y_high)      &
                      (nonzerox_yellow >= win_xright_low)   &
                      (nonzerox_yellow <  win_xright_high)).nonzero()[0]

        left_lane_inds.append(good_left)
        right_lane_inds.append(good_right)

        if len(good_left)  > minpix:
            leftx_current  = int(np.mean(nonzerox_white[good_left]))
        if len(good_right) > minpix:
            rightx_current = int(np.mean(nonzerox_yellow[good_right]))

    left_lane_inds  = np.concatenate(left_lane_inds)
    right_lane_inds = np.concatenate(right_lane_inds)

    leftx  = nonzerox_white[left_lane_inds];   lefty  = nonzeroy_white[left_lane_inds]
    rightx = nonzerox_yellow[right_lane_inds]; righty = nonzeroy_yellow[right_lane_inds]

    left_fit = right_fit = None

    if len(leftx) > 0 and len(lefty) > 0:
        left_fit  = np.polyfit(lefty, leftx, 2)
    if len(rightx) > 0 and len(righty) > 0:
        right_fit = np.polyfit(righty, rightx, 2)

    ploty      = np.linspace(0, img_h - 1, img_h)
    left_fitx  = (left_fit[0]  * ploty**2 + left_fit[1]  * ploty + left_fit[2]) if left_fit  is not None else None
    right_fitx = (right_fit[0] * ploty**2 + right_fit[1] * ploty + right_fit[2]) if right_fit is not None else None

    warp_zero  = np.zeros((img_h, img_w), dtype=np.uint8)
    color_warp = np.dstack((warp_zero, warp_zero, warp_zero))

    if left_fitx is not None and right_fitx is not None:
        pts_l = np.array([np.transpose(np.vstack([left_fitx,  ploty]))])
        pts_r = np.array([np.flipud(np.transpose(np.vstack([right_fitx, ploty])))])
        pts   = np.hstack((pts_l, pts_r)).astype(np.int32)
        cv2.fillPoly(color_warp, [pts], (0, 255, 0))
        
        y_eval       = img_h - 1
        left_x       = left_fit[0]*y_eval**2  + left_fit[1]*y_eval  + left_fit[2]
        right_x      = right_fit[0]*y_eval**2 + right_fit[1]*y_eval + right_fit[2]
        lane_center  = (left_x + right_x) / 2
        image_center = img_w / 2
        offset       = lane_center - image_center
        confidence   = max(0, min(1, 1 - (abs(offset) / image_center)))
    else:
        confidence = 0


    newwarp         = cv2.warpPerspective(color_warp, Minv, (img_w, img_h))
    result          = cv2.addWeighted(original, 1, newwarp,    0.5, 0)
    birdeye_overlay = cv2.addWeighted(birdseye, 1, color_warp, 0.5, 0)

    lane_debugs = {
        "Bird's Eye View":  birdseye,
        "White Mask": cv2.cvtColor(mask_white,    cv2.COLOR_GRAY2BGR),
        "Yellow Mask": cv2.cvtColor(mask_yellow,   cv2.COLOR_GRAY2BGR),
        "Combined Mask": cv2.cvtColor(combined_mask, cv2.COLOR_GRAY2BGR),
        "Sliding Windows": out_img,
        "Bird's Eye Lane": birdeye_overlay,
    }

    return result, lane_debugs, confidence

# ─── PROCESS FRAME ────────────────────────────────────────
def process_frame(frame):
    orig_h, orig_w = frame.shape[:2]


    yolo_frame, yolo_debugs, detections        = run_object_detection(frame)
    lane_frame, lane_debugs, confidence_score  = run_lane_detection(frame)

    yolo         = cv2.resize(yolo_frame, (orig_w, orig_h), interpolation=cv2.INTER_LINEAR)
    slidingwindow = cv2.resize(lane_frame, (orig_w, orig_h), interpolation=cv2.INTER_LINEAR)
    combined     = cv2.addWeighted(yolo, 1, slidingwindow, 0.4, 0)

    all_frames = {"Unity | YOLO + Lane Detection": combined}
    all_frames.update({"original":frame})
    all_frames.update({"Object Detection": yolo})
    all_frames.update({"Lane Detection":slidingwindow})
    all_frames.update(lane_debugs)
    all_frames.update(yolo_debugs)

    return all_frames, confidence_score, detections

# ─── SOCKET HELPERS ───────────────────────────────────────
def recv_exact(conn, n):
    buf      = bytearray(n)
    view     = memoryview(buf)
    received = 0
    while received < n:
        count = conn.recv_into(view[received:], n - received)
        if count == 0:
            return None
        received += count
    return bytes(buf)

# ─── CLIENT HANDLER ───────────────────────────────────────
def handle_client(conn, addr):
    conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)

    hs = recv_exact(conn, 8)
    if hs is None:
        conn.close()
        return
    img_w, img_h = struct.unpack('>II', hs)
    channels     = 3
    frame_bytes  = img_w * img_h * channels
    conn.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, frame_bytes * 4)

    print(f"[+] Unity connected from {addr}  {img_w}x{img_h}")

    frame_count = 0

    try:
        while True:
            header = recv_exact(conn, 4)
            if header is None:
                break
            frame_len = struct.unpack('>I', header)[0]

            if frame_len != frame_bytes:
                print(f"[!] Bad frame size {frame_len}, expected {frame_bytes}")
                recv_exact(conn, frame_len)
                continue

            raw = recv_exact(conn, frame_len)
            if raw is None:
                break

            frame_count += 1

            frame_rgb = np.frombuffer(raw, dtype=np.uint8).reshape(img_h, img_w, 3)
            frame_bgr = cv2.flip(cv2.cvtColor(frame_rgb, cv2.COLOR_RGB2BGR), 0)

            if frame_count % SKIP_FRAMES == 0:
                all_frames, confidence_score, detections = process_frame(frame_bgr)
                send_response(conn, detections, confidence_score)

                # ── Write every window to its own video ────────────
                if SAVE_VIDEO:
                    for window_name, frame_data in all_frames.items():
                        writer = get_writer(window_name, frame_data)
                        writer.write(frame_data)

                try:
                    display_queue.put_nowait(all_frames)
                except queue.Full:
                    pass
            else:
                send_response(conn, [], 0.0)

    except Exception as e:
        print(f"[!] Error: {e}")
    finally:
        conn.close()
        release_all_writers()
        display_queue.put(None)
        print(f"[-] Disconnected: {addr}  total frames: {frame_count}")

# ─── ACCEPT LOOP ──────────────────────────────────────────
def accept_loop(server):
    while True:
        try:
            conn, addr = server.accept()
            t = threading.Thread(target=handle_client, args=(conn, addr), daemon=True)
            t.start()
        except OSError:
            break

# ─── MAIN ─────────────────────────────────────────────────
def main():
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    server.bind((HOST, PORT))
    server.listen(5)
    print(f"[*] Server listening on {HOST}:{PORT} — waiting for Unity...")

    t = threading.Thread(target=accept_loop, args=(server,), daemon=True)
    t.start()

    print("[*] Press Q to quit")
    while True:
        try:
            frames = display_queue.get(timeout=0.05)

            if frames is None:
                cv2.destroyAllWindows()
            else:
                for window_name, img in frames.items():
                    cv2.imshow(window_name, img)

        except queue.Empty:
            pass

        if cv2.waitKey(1) & 0xFF == ord("q"):
            break

    print("[*] Shutting down")
    server.close()
    cv2.destroyAllWindows()

if __name__ == "__main__":
    main()
