export interface MaintenanceLogDto {
  id: number
  taskName: string
  jobRef: string | null
  sourcePath: string | null
  destPath: string | null
  outcome: 'success' | 'skipped' | 'error'
  message: string | null
  runAt: string
}

export interface BatchcutEventDto {
  id: number
  batchcutPathId: number
  folderName: string
  jobRef: string | null
  detectedAt: string
  status: 'applied' | 'error' | 'duplicate'
  errorMessage: string | null
}
