<template>
  <div class="wrap">
    <header class="header">
      <h1>Web Basic CW Fixer</h1>
      <!-- <p class="muted">ConceptWave metadata XML – Script lint</p> -->
    </header>

    <section class="card">
      <h2>1) XML Yükle</h2>

      <div class="row">
        <input type="file" accept=".xml" @change="onFileChange" />
        <button
          class="primary"
          :disabled="!file || uploading || running"
          @click="startAnalyze"
        >
          Analiz Et
        </button>
      </div>

      <div v-if="file" class="muted">
        Seçilen: <b>{{ file.name }}</b> ({{ formatBytes(file.size) }})
      </div>

      <div v-if="uploading" class="progress">
        <div class="bar" :style="{ width: uploadProgress + '%' }"></div>
      </div>
      <div v-if="uploading" class="muted">Upload: {{ uploadProgress }}%</div>

      <div v-if="error" class="error">
        {{ error }}
      </div>
    </section>

    <section class="card" v-if="jobId">
      <h2>2) Job Durumu</h2>

      <div class="status">
        <div><b>JobId:</b> {{ jobId }}</div>
        <div class="status-line">
          <span class="status-label">Status:</span>
          <span :class="['status', jobStatus.toLowerCase()]">
            {{ jobStatus }}
          </span>
        </div>
        <div><b>Progress:</b> {{ jobProgress }}%</div>
        <div class="muted" v-if="jobMessage">{{ jobMessage }}</div>
        <div class="error" v-if="jobError">{{ jobError }}</div>
      </div>

      <div class="row" style="margin-top: 12px">
        <button :disabled="!hasLog" @click="downloadLog">Log indir</button>
        <button :disabled="jobStatus !== 'Done'" @click="refreshIssues">
          Issue’ları yenile
        </button>
      </div>
    </section>

    <section class="card" v-if="jobStatus === 'Done'">
      <h2>3) Issue Listesi</h2>

      <div
        class="row"
        style="justify-content: space-between; align-items: center"
      >
        <div class="muted">
          Toplam: <b>{{ totalIssues }}</b> • Sayfa: <b>{{ page }}</b>
        </div>

        <div class="row" style="gap: 8px">
          <button :disabled="page <= 1" @click="goPage(page - 1)">
            Önceki
          </button>
          <button
            :disabled="page * pageSize >= totalIssues"
            @click="goPage(page + 1)"
          >
            Sonraki
          </button>
        </div>
      </div>

      <div class="tableWrap" v-if="issues.length">
        <table>
          <colgroup>
            <col style="width: 140px" />
            <col style="width: 260px" />
            <col style="width: 70px" />
            <col style="width: 70px" />
            <col style="width: 320px" />
            <col style="width: 600px" />
          </colgroup>
          <thead>
            <tr>
              <th>Rule</th>
              <th>Script</th>
              <th>Line</th>
              <th>Col</th>
              <th>Message</th>
              <th>Snippet</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(it, idx) in issues" :key="idx">
              <td>
                <code>{{ it.rule }}</code>
              </td>
              <td>{{ it.fullName }}</td>
              <td>{{ it.line }}</td>
              <td>{{ it.column }}</td>
              <td>{{ it.message }}</td>
              <td class="snippet">
                <code>{{ it.snippet }}</code>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-else class="muted">Issue yok 🎉</div>
    </section>
  </div>
</template>

<script setup>
import axios from "axios";
import { onBeforeUnmount, ref } from "vue";

const file = ref(null);
const error = ref("");

const uploading = ref(false);
const uploadProgress = ref(0);

const jobId = ref("");
const jobStatus = ref("");
const jobProgress = ref(0);
const jobMessage = ref("");
const jobError = ref("");
const hasLog = ref(false);

const issues = ref([]);
const totalIssues = ref(0);
const page = ref(1);
const pageSize = ref(100);

let pollTimer = null;

function onFileChange(e) {
  error.value = "";
  const f = e.target.files?.[0];
  if (!f) {
    file.value = null;
    return;
  }
  if (f.size > 90 * 1024 * 1024) {
    error.value = "Dosya çok büyük. Max 90MB.";
    file.value = null;
    return;
  }
  file.value = f;
}

async function startAnalyze() {
  if (!file.value) return;

  // reset UI state
  error.value = "";
  jobError.value = "";
  issues.value = [];
  totalIssues.value = 0;
  page.value = 1;

  uploading.value = true;
  uploadProgress.value = 0;

  try {
    const form = new FormData();
    form.append("file", file.value);

    const res = await axios.post("/api/analyze", form, {
      headers: { "Content-Type": "multipart/form-data" },
      onUploadProgress: (p) => {
        const total = p.total || file.value.size || 1;
        uploadProgress.value = Math.min(
          100,
          Math.round((p.loaded * 100) / total),
        );
      },
    });

    jobId.value = res.data.jobId;
    uploading.value = false;

    await pollJob(); // ilk anlık durum
    startPolling(); // sonra düzenli polling
  } catch (e) {
    uploading.value = false;
    error.value = extractErr(e) || "Upload/Analyze sırasında hata.";
  }
}

function startPolling() {
  stopPolling();
  pollTimer = setInterval(async () => {
    await pollJob();
    // done ise polling durdurup issues çek
    if (jobStatus.value === "Done" || jobStatus.value === "Error") {
      stopPolling();
      if (jobStatus.value === "Done") await refreshIssues();
    }
  }, 1000);
}

function stopPolling() {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
}

async function pollJob() {
  if (!jobId.value) return;
  const res = await axios.get(`/api/jobs/${jobId.value}`);
  jobStatus.value = res.data.status;
  jobProgress.value = res.data.progress;
  jobMessage.value = res.data.message || "";
  jobError.value = res.data.error || "";
  hasLog.value = !!res.data.hasLog;
}

async function refreshIssues() {
  await fetchIssues(page.value);
}

async function goPage(p) {
  page.value = p;
  await fetchIssues(p);
}

async function fetchIssues(p) {
  if (!jobId.value) return;
  const res = await axios.get(`/api/jobs/${jobId.value}/issues`, {
    params: { page: p, pageSize: pageSize.value },
  });

  totalIssues.value = res.data.total;
  issues.value = res.data.items || [];
}

async function downloadLog() {
  if (!jobId.value) return;

  const res = await axios.get(`/api/jobs/${jobId.value}/log`, {
    responseType: "blob",
  });

  const blob = new Blob([res.data], { type: "text/plain" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `webBasicCWFixer_${jobId.value}.log`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

function formatBytes(bytes) {
  const units = ["B", "KB", "MB", "GB"];
  let b = bytes;
  let u = 0;
  while (b >= 1024 && u < units.length - 1) {
    b /= 1024;
    u++;
  }
  return `${b.toFixed(u === 0 ? 0 : 1)} ${units[u]}`;
}

function extractErr(e) {
  // axios error
  const msg = e?.response?.data?.detail || e?.response?.data || e?.message;
  if (typeof msg === "string") return msg;
  try {
    return JSON.stringify(msg);
  } catch {
    return null;
  }
}

onBeforeUnmount(() => stopPolling());
</script>
