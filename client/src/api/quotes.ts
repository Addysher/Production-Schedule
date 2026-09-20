import { apiClient } from './client'
import type { QuoteEstimateRequest, QuoteEstimateResponse } from '../types/api'

export const quotesApi = {
  estimate: (request: QuoteEstimateRequest) =>
    apiClient.post<QuoteEstimateResponse>('/quotes/estimate', request).then((r) => r.data),
}
