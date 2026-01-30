<template>
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
      <button :disabled="!hasLog" @click="onDownloadLog">Log indir</button>
      <button :disabled="jobStatus !== 'Done'" @click="onRefreshIssues">
        Issue’ları yenile
      </button>
    </div>
  </section>
</template>

<script setup>
defineProps({
  jobId: {
    type: String,
    default: "",
  },
  jobStatus: {
    type: String,
    default: "",
  },
  jobProgress: {
    type: Number,
    required: true,
  },
  jobMessage: {
    type: String,
    default: "",
  },
  jobError: {
    type: String,
    default: "",
  },
  hasLog: {
    type: Boolean,
    required: true,
  },
  onDownloadLog: {
    type: Function,
    required: true,
  },
  onRefreshIssues: {
    type: Function,
    required: true,
  },
});
</script>
