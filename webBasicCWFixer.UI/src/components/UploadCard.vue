<template>
  <section class="card">
    <h2>1) XML Yükle</h2>

    <div class="row">
      <input type="file" accept=".xml" @change="onFileChange" />
      <button
        class="primary"
        :disabled="!file || uploading"
        @click="onStartAnalyze"
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
</template>

<script setup>
defineProps({
  file: {
    type: Object,
    default: null,
  },
  uploading: {
    type: Boolean,
    required: true,
  },
  uploadProgress: {
    type: Number,
    required: true,
  },
  error: {
    type: String,
    default: "",
  },
  onFileChange: {
    type: Function,
    required: true,
  },
  onStartAnalyze: {
    type: Function,
    required: true,
  },
  formatBytes: {
    type: Function,
    required: true,
  },
});
</script>
